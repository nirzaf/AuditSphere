# Current-slice archive, October 2026

> **STATUS: HISTORICAL.** Sections moved verbatim from `auditsphere-execution-current-slice.md` on 2026-10-08 to keep the current handoff small. They are the audit trail of earlier slices. Never edit them to change a verdict; record a new observation in the current slice or in `status.json`.

## Certification administrative review integration

Exact-source applicability rules, append-only review persistence and independent administrator publication are implemented. Current evidence and commit identities are in `status.json` under `completePracticeManagement.certificationReview`. The read workspace, API and Angular review dialog have focused verification, including a synthetic browser verification/withdrawal flow. Staffing eligibility readers are integrated and focused checks pass; deterministic revocation/assignment concurrency and broader acceptance remain unfinished; existing assignment history is preserved and legacy qualifications are not automatically verified. Only the synthetic browser database received the review migration; live/development databases remain unchanged. Client portal write-gate corrections are committed separately, with focused evidence in the crosswalk.

## Resource range safety

Grid/allocation commands refuse unsupported dates and week counts before arithmetic. Focused Domain/API and contract checks pass; browser refusal and valid-range recovery pass. Certification entries explicitly remain unverified. Pure exact-source applicability checks pass; persistence, review actions and staffing integration remain unimplemented. Broader staffing acceptance remains open. See execution ledger.

## Resource capacity reconciliation

Analytics and the weekly grid preserve all configured weekly minutes using one daily allocator, including leave deductions. Focused pure/PostgreSQL checks pass; positive browser and broader acceptance remain open. See execution ledger.

## Retained firm expense paging and recovery

Firm books now exposes bounded expense pages and exact firm-scoped expense lookup for uncertain action recovery. Focused database, API and Angular checks pass; the positive browser finance journey remains pending because the synthetic browser identity has no finance assignment. No access grant changed. Exact source/results are in the execution ledger.

## Angular quality and development-speed specification follow-up (2026-10-22)

Tasks 01–06 are implemented. The latest follow-up fixes observed keyboard focus loss after removing a draft consolidation journal row. The affected component spec, changed-file lint and production Angular build pass. Exact source, results, the unrelated style-budget warning and CI evidence are in [`status.json`](status.json#targetedVerification.angularQuality20261022).

Task 07 / AC-09 remains partial. Built-in browser checks exercised the synthetic staff shell, empty journal queue and existing consolidation draft form. The focus correction passed a built-in browser recheck. Planning leave-dialog focus, fieldwork tab/validation behavior and narrow-screen reduced-motion checks also passed within the available fixture. Populated editors, approved-budget/procedure review, client shell, native zoom and remaining state coverage are incomplete. Owner review/approval was reported; actual AT/browser versions and workflow findings remain requested.

The earlier exact-source full release pass is retained as baseline evidence. A subsequent nightly exposed asynchronous snapshot/navigation readiness failures; bounded test corrections preserve the isolation, final-path, revocation and persistence assertions and pass the focused E2E cohort. Current-source CI remains in progress; full suites run only in CI/CD. No live tenant or production data was changed.

## Firm monthly date boundaries

Monthly P&L safely handles supported year-0001 and December-9999 periods, preserving null missing comparatives. Monthly series above the existing 1,200-row UI boundary are refused before activity calculation. The existing balanced-ledger regression passes; full acceptance remains open. Exact results are in the execution ledger.

## Task queue source lineage

Queue rows now expose retained source keys and exact template identities in all layouts. Cards also retain FSLI/mapping, PBC and reviewer context plus task activity. Focused lineage/component checks, builds and manual-source browser inspection passed; template browser and responsive acceptance remain open. See execution ledger.

## Queue prerequisite presentation

The queue now displays a separate blocked prerequisite label consistently in list, board and calendar views using existing server prerequisite facts. Missing viewer permission alone is not a blocker; completed history stays unchanged. Focused component checks and Angular/API builds passed; exact evidence is in `status.json`. Follow-up repaired omitted safety rows in the synthetic seed while preserving application guards. Browser creation, assignment, reviewed dependency, blocked display in all three layouts and clearing after predecessor completion now pass. Responsive and broader WF-06 acceptance remain unverified.

## API and Angular parity: complete member discovery

Matching counterparties can now be discovered through bounded pages and search. Exact seller/buyer resolution retains selections across pages, and the outside-party path rejects an eligible member even when off page. The shared API exception envelope is documented once at the UI group boundary. Focused checks and synthetic browser search passed; see `status.json` under `apiAngularParity.localVerification.matchingOptionsSlice`. Full acceptance remains pending the original task crosswalk and current-source CI/CD evidence.

## API and Angular parity: bounded scope continuation

Components and readiness now have independent bounded pages and complete counts. Exact component correction remains available when the selected component is outside the displayed page. The focused browser check loaded the current contract and retained evidence using the synthetic local database with external effects disabled. This does not establish multi-page browser or full workflow acceptance. Current verification evidence and remaining gates are in `status.json` under `apiAngularParity.localVerification.pagingSlice`. Continue the original parity specification; do not equate this slice with completion of all tasks.

## Current allocated-cash gate and retained settlement history

Advance-dependent activation, lifecycle, portal eligibility, balance drafting and bundle gates now share a current-settlement predicate. A stored PAID marker alone is insufficient: the invoice must be posted and match client/currency/amount, with sufficient current net cash after approved reversals. Credit-adjusted balance settlement retains its existing policy. Staff reads project the current unpaid state while original paid milestone fields and receipt bytes remain immutable. Settlement publication reacquires the existing firm fence before checking cash and first publishing PAID evidence. No database trigger was weakened.

The focused commercial/reversal/activation cohort and the corrected split-receipt case have incremental passing evidence; exact code identity and limitations are in `status.json` under `completePracticeManagement.advanceCashGate`. Current-source full regression and the browser financial journey remain acceptance work. Continue the source-backed schedule and integration gaps, including the returned-time lifecycle in RES-04, plus the independent API/Angular parity backlog; this is not full product acceptance.

## Complete practice-management inventory and parity regression repair

The supplied complete-product specification is preserved under requirements and linked to the existing PM crosswalk. PMCOMP-00 source reconciliation is complete: the [criterion audit](auditsphere-execution-report-practice-management-criterion-audit-current.md) maps all 87 ASPM-qualified criteria to current source/test-body findings, residuals and legacy aliases. Test bodies were inspected, not executed, and release acceptance remains `NOT_VERIFIED`; CI, methodology, owner and live-provider gates remain open.

Full CI exposed an untranslatable task-assignee projection and two incorrect history/receipt assertions. The assignee query now deduplicates/orders an anonymous projection before constructing its DTO; portal history checks the last retained page; authorized sibling-client receipt recovery explicitly returns no foreign receipt. The affected focused cohort passed. Exact source/results and preceding CI failure evidence are in `status.json`. Continue criterion reconciliation and exact-SHA release verification; no production acceptance is claimed.

## API and Angular parity: task implementation verification complete (T025, T028)

Both remaining parity implementation tasks from the 51-task specification have been verified with targeted checks:
- **T025 (Resolve component replacement retention before exposing correction):** In-place destructive component replacement is declined in favor of immutable new scope versions preserving original scope and component evidence. The correction handoff in `overview.ts` and `component-correction.ts` enforces selecting the identical reporting period and group from authorized options. Verified via `component-correction.spec.ts`.
- **T028 (Complete external component-pack intake, review and pinning):** Five external pack operations (submit with exact digest/hashes, reconcile, return with reason, independent approval, and pinning to scope) are fully implemented and verified. Unit tests in `external-packs.spec.ts` and scope workspace integration in `scope-workspace.spec.ts` ensure exact SHA-256 digests, six-place decimal bounds, pagination, and independent review. The PostgreSQL integration test in `ConsolidationWorkspaceApiTests` validates concurrent review serialization and retained lineage.

All 51 parity tasks in `status.json` and `auditsphere-execution-tracker-api-angular-parity-current.md` now have `implementationStatus: IMPLEMENTED_VERIFICATION_PENDING`. Full acceptance remains subject to CI/CD release verification.

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

## Immutable client sales invoice preparation

Native client books now offer untaxed, same-currency sales drafts with exact decimal strings, client seller/customer snapshots, document/accounting/supply/due dates, selected income accounts and declared calculation inputs. Each maker edit appends a revision with concurrency checks; previous content remains immutable. A repeated source reference on a different invoice in the same client book produces a preserved warning. Drafts create no client or firm journal, receivable, issued document or delivery event.

Angular locks an unknown save intent and recovers the original actor/request receipt without another POST. A confirmed absence permits an explicit unchanged retry. Current scoped authorization protects historical reads and recovery. Withdrawing the accepted bookkeeping service prevents new drafts and edits while retaining authorized historical reads and exact replay. Focused PostgreSQL, Angular, API contract and automated Chromium checks passed; EF reports no model drift. The Release solution build passed. Exact evidence, process handles, migration and limits live in `verification.clientSalesInvoiceDrafts` in `status.json`.

This remains partial invoice preparation. Seller address/legal issuance configuration, approved money/tax policy, verified source-document linkage, draft discovery, submission/review, native ledger posting/open items, rendering and delivery remain outstanding. Tax and ancillary capabilities are optional; untaxed preparation requires no VAT module. The full epic remains active and its final merge awaits completion. The independent calculator-baseline regression is still running; its result must not be attributed to this newer schema/workflow.

## Shared native invoice and credit calculation foundation

A pure client invoice/credit engine now calculates declared quantity, unit price, discount, line net, additive tax components, gross and separately disclosed signed rounding residuals. Precision and midpoint behavior are explicit caller inputs. Untaxed and zero-rated treatments are distinct; an untaxed line requires no tax component or optional tax-module activation. Inclusive residuals require a named bounded adjustment policy, and unsupported FX is refused without an assumed rate.

The native journal and invoice calculators share a neutral storage amount guard with unchanged exact native journal semantics. Focused arithmetic and PostgreSQL journal regressions passed. The fixed-commit Angular build and serialized Release solution build passed, and the full PostgreSQL-backed solution regression is running in an independent verification checkout. The exact source, process handle, commands and observed evidence are in `verification.clientInvoiceCalculation` in `status.json`; resume that live handle rather than starting another run.

This is calculation machinery, not an available client invoice workflow or approved tax policy. Persisted policy approval, native journal policy fencing, verified source-document linkage, posting/open items, frozen historical rendering and delivery remain outstanding. Tax and ancillary capabilities remain optional. The full user-story objective remains active and its final merge is pending completion.

## Reviewed client counterparty contact-detail amendments

Client counterparties now support immutable proposed contact-detail revisions and independent append-only approval/rejection decisions. Pending work does not change the effective profile. Approval requires the exact next revision and a reviewer different from the proposer; client locking and database guards fence competing approvals. Effective list/history reads project approved details while retaining the original saved profile and all proposals/decisions.

Angular provides progressive amendment preparation, independent review and paged history. It binds effective details to the exact approved proposal, clears assent when inputs change, blocks another command after an unknown outcome until fresh history and an explicit reset, and clears invalid sessions without a history request loop. Backend concurrency/history checks, focused Angular checks, actual API contract generation and the complete native journal browser journey passed. Exact source, commands, counts and limits are in `verification.clientCounterpartyAmendments` in `status.json`.

Legal identity, role, country, currency default and external identity remain fixed in this editor. Original-profile presentation, reviewed merges/reclassification and invoice posting snapshots remain outstanding. Tax remains optional. Another process integrated the initial amendment source and removed its checkout before final verification; verification and recovery fixes were completed in a recreated managed worktree. This does not establish completion of the user-story epic or authorize its final merge.

## Client customer and supplier profile foundation

Native client books now offer scoped customer/supplier profiles with optional tax and ancillary details. A combined role appears in both customer and supplier filters without financial netting. Creating a profile requires the current accepted bookkeeping service; retained profiles remain readable under current client authority after that service is withdrawn. Scoped external identities and normalized names fence duplicate creation. Database triggers prevent overwriting or deleting saved profiles.

Angular exposes creation, explicit refresh, role filters and progressive saved details. Unknown creation outcomes require inspecting retained records before an explicit separate draft. Backend scope, duplicate, mandate and history checks, Angular contract checks, actual API contract generation and the browser journal journey passed locally. Exact source, counts and verification limits are in `verification.clientBookkeepingCounterparties` in `status.json`.

This is a partial counterparty foundation. Reviewed amendments, duplicate resolution, merges/reclassification, frozen invoice profiles and AR/AP open items remain outstanding. The broader user-story implementation remains active; VAT/tax and other ancillary modules remain optional and do not gate core bookkeeping. No final epic merge, production migration or external acceptance has been claimed.

## Ordinary native draft editing and preview fencing

The original assigned preparer can now edit an ordinary unsubmitted native journal's description, accounting date and balanced posting lines within its existing open period. Editing locks the journal and period, checks the current accepted mandate and approved posting accounts, advances the revision and replaces draft lines atomically. Earlier previews cannot authorize submission of the edited content. Returned rework continues to require the matching immutable submission and independent return decision; submitted and posted content remains protected. Full-reversal drafts retain their exact original accounting sides and are excluded from general draft editing.

Angular offers an explicit draft editor, clears the earlier preview and assent, and requires a new server preview after saving. Journal number, period and currency remain fixed. Dynamic backend concurrency, stale-preview, scope and immutable-content checks passed; the browser journey verifies edited amounts and fresh revision along with existing posting, reversal, Trial Balance and inactive-service behavior. Exact source, commands, counts and verification limits are in `verification.clientOperationalDraftEditing` in `status.json`.

A concurrent process integrated the implementation and removed its checkout during verification. The integrated source and final amount assertion were subsequently checked in an isolated verification checkout; this is local slice evidence, not full epic acceptance. Native opening postings, cutover/carry-forward, evidence-byte capture, currency policy, invoices/open items, reporting integration and stable exports remain outstanding. Tax and ancillary modules stay optional. No final epic merge was performed by this task.

## Saved native journals and inactive-service history

The native Angular workspace now discovers saved journals through client-scoped period/status filters and paged results. Users reopen the exact saved journal from its number without copying a GUID. Counts and rows use one repeatable-read transaction; each page or refresh is a current-state query, not a retained financial-report snapshot. Decoding fences client, period, status, page, journal identity and service-state changes. Navigation to details rechecks current authorization.

Stopping the bookkeeping service no longer prevents authorized reads of posted native GL/TB. Saved journal lists and reports disclose the inactive service; history, snapshots and original-actor posting receipts remain available under existing scoped role checks. New commands still require a current accepted mandate, and Angular disables mutation actions after observing inactive state. Revoking a role denies history rather than converting service suspension into an access grant. No data is deleted or reconstructed.

Focused PostgreSQL, Angular, API and automated Chromium evidence lives in `verification.clientOperationalJournalDiscovery` in `status.json`. Browser verification includes retained submitted history and Trial Balance after service disablement. The broader epic remains incomplete: native opening postings, cutover/carry-forward, invoices/open items, reporting integration, evidence-byte capture, stable report exports and other outstanding criteria remain active. Tax and ancillary modules stay optional; no final merge or production acceptance was performed by this task.

## Native activity Trial Balance and report date selection

The native ledger report now returns a Trial Balance from the same repeatable-read database snapshot as its GL rows and full-result movement totals. Users select a reporting period independently of journal preparation, choose dates within that period and optionally include approved chart accounts with no posted activity. Per-account opening activity, period debits/credits and closing balances reconcile exactly. Only posted native journals contribute. Angular validates exact decimal strings, unique account identities, row equations and overall controls, and fences stale filter responses.

This is a period-activity projection: opening means posted activity earlier in the same selected period. Reviewed cutover/opening postings, prior-period carry-forward, immutable native-source acceptance, adjusted views, comparative mapping and durable exports remain outstanding. The initial slice used independent page snapshots; cross-request paging is now fenced by the posting sequence described below. Tax and ancillary modules remain optional. Focused PostgreSQL, Angular, API and automated browser verification passed; exact evidence is in `verification.clientOperationalTrialBalance` in `status.json`. Full epic acceptance and final merge remain pending.

## Stable native GL pages across concurrent postings

The native GL/TB response now includes a string-valued posting-sequence high-water mark. Page zero captures it inside the report's repeatable-read transaction; later pages must return it. All movement rows and report-wide account totals use that same ceiling, and stable ordering includes immutable journal identity before line number. A later posting cannot enter an existing report page sequence even when its accounting date sorts before already displayed rows. The database assigns sequence values under a transaction-scoped advisory lock held through commit, preventing a slower concurrent posting from becoming visible later below an observed ceiling. Existing clients and imported/adjusted reporting sources are unchanged.

Focused PostgreSQL coverage posts a backdated journal after the first page and verifies page two retains the original line set, full-result count and Trial Balance. Angular transport/component tests passed, and the real API OpenAPI contract tests passed with the new cursor parameter; the generated contract artifact was refreshed. Production Angular build exited 134 without diagnostics, EF drift inspection is still pending, and browser journey coverage has not run in this slice. Account/date/source/reference/party/dimension filters, scoped posted-document/evidence drill-through, large durable exports, native opening/carry-forward, and imported/adjusted source views remain outstanding. Tax and ancillary modules remain optional; full epic acceptance and final merge remain pending.

## Native manual journal full reversals

Posted native manual journals now support a full reversal in an explicitly selected open correction period. The original remains posted; immutable lineage retains the reason and evidence reference, and database guards require exact swapped debit and credit lines. Identical concurrent preparation requests return the same draft. The reversal follows the normal preview, submission, independent approval and posting workflow. Journal and general-ledger readback show both directions of the correction relationship.

Focused backend, Angular, API contract and automated browser checks passed; EF reports no pending model changes. Exact evidence is recorded in `status.json`. This slice retains an evidence reference rather than uploaded evidence bytes. Original frozen accounts must remain approved for the correction date; unsupported chart replacement fails closed. Invoice credits, settlement and refund recording, opening balances, reporting bridges and other requested capabilities remain outstanding. Tax and ancillary modules remain optional. A concurrent process integrated the branch and removed its checkout before evidence recording; work continues in a fresh managed worktree. This is not full epic or production acceptance.

## Native journal exact posting receipts and recovery

Native posting now requires a stable client-scoped command identity and a hash of
the exact journal, actor, submitted revision, preview and review reason. Identical
concurrent commands recover the same immutable outcome; different intent using the
same key is refused. A new key cannot claim another posting of an already posted
journal. The posting, approval and receipt commit together, with final scope and
mandate revalidation. Scoped recovery is available to the original assigned actor.

Angular retains an unknown posting request and its original identity. The user can
retrieve the committed receipt or explicitly retry that same reviewed request if
no receipt is found. Original amounts and reason remain inspectable. Confirmed
server refusals require a fresh saved-journal preview; transport/readback failures
stay unknown. This adds no generic automatic retry to other commands.

Focused database rollback/concurrency, Angular contracts, browser loss/recovery,
API and migration evidence lives in `verification.clientOperationalPostingReceipts`
in `status.json`. Draft creation/other command receipts, numbering policy, general
draft editing, evidence, reversals, currency/FX policy, invoices/open items, opening
balances and native TB/reporting bridge remain open. The full epic and final merge
remain pending. Tax and other nonmandatory modules remain optional.

## Native journal returned-content rework

The original preparer can edit a returned journal's description, date and balanced
posting lines within its existing open period. The command locks the current
revision and period, checks the accepted bookkeeping mandate and approved chart,
and saves a new draft revision atomically. The database requires the prior returned
content to match its immutable submission and independent return decision. Journal
number, period and currency stay fixed. Earlier submitted content and review
reasons remain intact; corrected content needs a fresh preview and independent
review before posting. Uncaptured historical returns cannot be reconstructed.

Angular exposes the returned editor and clears its context when the selected
client or session changes. Focused synthetic database, browser, Angular, API and
migration evidence is recorded in `verification.clientOperationalJournalRework`
in `status.json`. General draft editing, reversal lineage, maker evidence, durable
receipts, currency/FX policy, invoices, open items and native TB/reporting snapshots
remain open. The full epic and final merge remain pending. Tax and other
nonmandatory modules remain optional and do not gate the core bookkeeping path.

## Native journal immutable submitted content

Submission now captures the journal header and ordered lines in an immutable
client-scoped content record inside the same database transaction. Subsequent
return, resubmission and posting preserve earlier captured versions. Database
triggers refuse direct snapshot inserts and all changes or deletions. The scoped
API returns exact decimal strings, and Angular exposes submitted versions through
progressive details. Existing uncaptured review content is not reconstructed;
the UI discloses its absence.

Focused database, UI, browser, contract and migration evidence lives under
`verification.clientOperationalJournalSnapshots` in `status.json`. This provides
the content-history foundation for rework; returned editing remains protected and
unimplemented. Reversal lineage, durable receipts, currency/FX policy, invoices,
open items and native TB/reporting snapshots remain open. Full epic completion,
regression and final merge are not established.

## Native journal independent return and review history

An independent assigned reviewer can return the exact submitted journal revision
with a reason. The return decision and status transition commit together; database
protection refuses a return without matching independent review evidence. Journal
details expose append-only decisions, reasons, reviewer identities and timestamps.
The Angular workbench shows the history and supports return, fresh-preview
resubmission and subsequent independent approval without duplicating ledger effect.

Returned content remains protected until immutable revision editing is implemented.
This slice verifies resubmission of preserved content, not corrected-content editing.
Evidence capture/revision snapshots, rework editing, reversals, durable receipts,
native reporting snapshots and client invoices remain open. Focused runtime and
contract evidence lives under `verification.clientOperationalJournalReturn` in
`status.json`. The full bookkeeping epic remains unmerged; tax and other
nonmandatory modules stay optional.

## Native journal authoritative preview and review binding

The native journal workspace now previews saved unposted journals without financial
writes. It returns normalized lines and exact server totals after validating the
current accepted service, approved chart/accounts and open matching period. Its
intent digest includes the journal revision/content and current accounting context.
Submit and approval recompute the digest under transaction locks and refuse missing
or stale previews. Angular exposes the server preview before the separate preparer
or reviewer consent and clears it when loading another saved journal.

Focused PostgreSQL, Angular, API-contract and browser evidence is under
`verification.clientOperationalJournalPreview` in `status.json`. This does not
complete journal evidence capture, editing/return/reversal workflows, durable
command receipts or currency/FX policy. Native TB/reporting snapshots and client
invoice/open-item capabilities remain open; the full epic remains unmerged.

## Native journal exact amount rules

Draft creation and submission/posting now share a pure native journal calculator.
Each line must fit the existing database precision without rounding, have exactly
one positive side, and contribute to exact balanced totals. Angular draft entry
uses the same storage bound. This prevents oversized decimal values from reaching
the database and avoids overflow while validating hostile input. Editing amounts
or adding/removing a line also visibly clears review consent; the browser journey
checks that valid correction requires fresh consent before saving.

This is storage and balance validation, not completion of the currency-policy
story. Configured currency scale, rounding policy, FX provenance, authoritative
preview/review digests and durable command receipts remain open. Verification is
recorded under `verification.clientOperationalJournalExactAmounts` in `status.json`.

## Native client journals — ledger integrity slice

The managed bookkeeping worktree now adds database protection for submitted and
posted native journals, balanced-line validation, independent approval evidence,
active approved-chart accounts, and period-close serialization. Application close
readiness reports pending native journals; closed periods refuse new drafts.
Posted ledger reads use a consistent database snapshot, and the Angular ledger
accepts signed net movements while retaining unsigned debit and credit values.
Focused verification evidence is recorded under
`verification.clientOperationalLedgerIntegrity` in `status.json`.

This is a partial implementation of the owner-requested epic. Native TB and
financial-package snapshot adaptation, opening balance integration, preview and
durable command receipts, correction lineage, client invoices and AR/AP remain
open. VAT/tax and other nonmandatory modules stay optional. This branch is not
ready for the requested final merge or production acceptance.

## Client bookkeeping epic — scope baseline

The owner-requested Ekwo-inspired user stories are suitable as a bounded extension
to this repository's modular monolith. The project scope now permits native client
bookkeeping only for specifically authorized services. Existing external-source
clients, firm billing, audit adjustments, and group consolidation retain separate
ownership. VAT/tax and ancillary modules are optional; client isolation, balanced
postings, human review, and immutable lineage are core requirements.

| Capability | Current reuse surface | Remaining gap for the requested client-bookkeeping mode |
|---|---|---|
| Client book setup, chart and periods | `ClientAccountingProfile`, reporting books/periods, chart/taxonomy services, and opening-balance workflows | Explicit managed-vs-external source mode, authorized service mandate, and reviewed cutover are not yet a complete workflow. |
| Double-entry ledger | Client reporting adjustments and external GL intake | The native journal slice now supports balanced drafts, independent posting, immutable posted evidence and period locks. Durable command receipts and correction lineage remain open. |
| GL/TB | Existing TB/GL import, completeness, query and calculation paths | A posted native movement view exists. Complete native TB/opening balances and accepted reporting snapshots while preserving external-source and adjusted-reporting distinctions. |
| Sales invoices and credits | `Practice` invoice/billing flows | Those invoices belong to the firm; client-owned sales documents and their ledger/open-item effects need a separate capability. |
| Purchase invoices and supplier credits | Document/PBC intake and client accounting evidence | Reviewed client AP documents, postings, and supplier open items are not a complete workflow. |
| VAT/tax (optional) | No verified client transaction-tax calculation and reconciliation workflow identified | Optional jurisdiction-specific, versioned rules and reviewed workings; do not select a production rate without owner approval. |
| Financial statements | Existing mappings, financial-package calculation, rendering, review and release | Reuse this pipeline through an accepted native-ledger snapshot adapter; preserve comparatives and audit-adjustment source reflection. |

This is a source-level reuse/gap inventory, not runtime or production acceptance
evidence. Delivery follows the approved incremental sequence: client book foundation,
controlled ledger, optional evidence/tax modules, invoices/open items, then reporting
and release. The implementation branch must keep existing external-source behavior
as the default.

## Completed focused slice: Client bookkeeping service-mode gate

The client profile persists an explicit source mode, defaulting to external source.
A reviewer can select native bookkeeping only when a client-level `BOOKKEEPING`
acceptance decision is accepted without unresolved conditions; existing reporting
periods block inline conversion and require a reviewed cutover. The matching
capability kind is recognized by Application validation and fails closed when its
service route is missing or only conditionally accepted. The Angular profile
workspace displays and submits the mode. This setup slice does not implement native
journal posting, invoices, AR/AP, or tax; those remain later stories. Focused .NET,
Angular, and browser results are recorded under `verification.clientBookkeepingSourceMode`
and `verification.clientBookkeepingCapabilityAuthorization` in `status.json`. Local
results do not establish production acceptance.

## Angular migration and Blazor retirement — NOT READY

## Completed focused slice: HTTP boundary hardening, OpenAPI contract, and IaC baseline

The architecture-hardening user story (HTTP trust boundary, canonical OpenAPI 3.1 contract,
production infrastructure as code) was implemented in verified slices through commit
`75005721` plus the rate-limit fix and IaC commits that follow it. Boundary and contract
evidence lives in `status.json` under `verification.httpBoundaryAndOpenApiContract`.

- **Slice A/B/C (HTTP boundary):** `/api/ui` and `/api/setup` require an authenticated cookie
  session before any handler runs; native ASP.NET Core rate limiting partitions by immutable
  `tid`/`oid` identity (IP fallback for anonymous sign-in) with per-class windows and export/upload
  concurrency budgets; centralized security headers use a build-derived strict script CSP;
  fingerprinted Angular assets cache immutably; oversized JSON commands receive a structured 413
  before business processing. `HttpBoundarySecurityTests` passed 12/12. The m365 administration
  class was raised from 10 to 30 permits/minute after the workspace provisioning flow proved
  10/min throttles legitimate setup wizards.
- **Slice D/E (OpenAPI):** native .NET OpenAPI generates the `auditsphere` 3.1 document from the
  actual endpoints; session-cookie security and boundary 401/429/413 responses are declared on
  protected operations; financial decimals stay exact strings. The committed contract artifact is
  drift-checked in CI by `scripts/contracts/verify-openapi.sh` (single no-database generation
  check; hosted-suite policy unchanged). `OpenApiContractTests` passed 5/5; Angular unit tests
  passed 500/500 including the new typed-429-refusal case.
- **Slice F/G/H/I (IaC):** Bicep modules + both environment definitions compile with
  `az bicep build`; Dockerfiles define immutable SHA-tagged API/worker/migration-bundle images;
  the dispatch-only release workflow encodes migration-before-traffic, 10% canary with read-only
  smoke checks, and promote/rollback. Nothing is deployed: live Azure steps remain
  BLOCKED_EXTERNAL pending owner-approved subscription, ACR, OIDC federation and Key Vault
  secrets (`infra/auditsphere-deployment-iac-runbook.md` records the verified/unverified split).

Known pre-existing failure (not this slice): `StaffingChangeReviewApiTests.
ExactReviewCsrfCurrentAuthorityReceiptsAndRetentionMigrationAreEnforced` fails with an
in-handler InvalidOperationException (503) identically at pre-change baseline commit `03d7eb13`
(verified via isolated worktree). Remaining story work: live deployment acceptance, canary and
rollback exercise, production OpenAPI exposure policy decision, and the real-AT walk-through.

## Completed focused slice: Firm-expense submit/review outcome recovery

At pushed code commit `d21d0382`,
`AngularFirmExpenseActionRecoveryJourneyTests.SubmitAndReviewRequirePersistedStateCheckAfterUnconfirmedResponses`
passed **1/1** in 39 seconds against isolated PostgreSQL and API-hosted
Angular sessions. A submit request aborted before reaching the API left the
expense `DRAFT`; the UI required an exact persisted-state read and explicit
acknowledgment before enabling a deliberate retry. A reviewer approval accepted
by the API with its browser response dropped was confirmed from persisted
`APPROVED` state without resending. PostgreSQL contains exactly one journal with
the expected reviewer and reason, and the browser recorded no page errors.

The Angular production build passed at 349.67 kB initial size with the existing
7.53 kB Commercial Settings stylesheet warning. This presentation/E2E slice did
not change the EF model. The full solution suite was not rerun; its latest
complete result remains 1001/1001 at `ead85032`. Expense-record creation after
an unknown response, remaining finance authorization/recovery cases, overall
AS-PAR-002 acceptance and Blazor retirement remain open.

## Completed focused slice: Firm-ledger close outcome recovery

At code/test commit `5afd38fe`,
`AngularFirmLedgerCloseOutcomeRecoveryJourneyTests.LostCloseResponseIsReconciledBeforeAnyExplicitRetry`
passed **1/1** in 36 seconds against isolated PostgreSQL and an API-hosted
Angular session. The first period close succeeded on the server while the
browser response was dropped; the UI blocked retry until an explicit persisted
read showed `CLOSED` at revision 2, and no duplicate close was sent. For a
second period, the request was aborted before reaching the API. The UI verified
the saved state remained `OPEN` at revision 1 and required an explicit
acknowledgment before the operator manually retried. PostgreSQL contains one
close decision per period with the reviewed reason, and there were no browser
page errors.

The Angular production build passed at 349.67 kB initial size with the
existing 7.53 kB Commercial Settings stylesheet warning. The EF model was not
changed. The full solution suite was not rerun; its latest complete result
remains 1001/1001 at `ead85032`.

## Completed focused slice: Firm-ledger role capability matrix

At code/test commit `356d48a9`,
`AngularFirmLedgerRoleMatrixJourneyTests.FinanceManagerCanReadButNotCloseWhileReviewerCanCloseAndPartnerIsDenied`
passed **1/1** in 42 seconds against isolated PostgreSQL and API-hosted
Angular sessions. The firm-wide FinanceManager reads the current period and
accounts but has no close action and receives HTTP 403 for a direct close.
The FinanceReviewer sees the close action; a Partner receives the safe denial
in Angular and HTTP 403 from the data endpoint with no finance markers. The
manager's refused close left the period open at revision 1 with no close
decision. No browser errors occurred.

The production Angular build passed at 349.67 kB initial size, with the
existing 7.53 kB Commercial Settings stylesheet budget warning. This
test-only slice did not change the EF model. The complete solution suite was
not rerun; its latest complete result remains 1001/1001 at `ead85032`.

## Completed focused slice: Client-scoped reviewer denied firm ledger

At code/test commit `2cee13da`,
`AngularFirmLedgerScopedReviewerJourneyTests.ClientScopedFinanceReviewerCannotReadOrCloseFirmWideLedgerPeriod`
passed **1/1** in 31 seconds against an isolated PostgreSQL fixture and an
API-hosted Angular session. A FinanceReviewer with a client-scoped grant sees
the safe firm-ledger denial and no period marker; direct firm-ledger read and
close requests both return HTTP 403. PostgreSQL confirms the firm period
remains `OPEN` at revision 1 with no close decision, and the browser reports
no page errors.

This test-only slice did not change the EF model. The full solution suite was
not rerun; its latest complete result remains 1001/1001 at `ead85032`.

## Completed focused slice: Invoice lifecycle lost-response recovery

At code/test commit `d7854fc8`,
`AngularInvoiceOutcomeRecoveryJourneyTests.AcceptedApprovePostAndSendWithLostResponsesRequireRefreshBeforeContinuing`
passed **1/1** in 33 seconds against isolated PostgreSQL and API-hosted
Angular browser sessions. The API accepted invoice approval, posting and
sending, while Playwright dropped each response. For each command Angular
showed the unresolved-state panel and kept actions blocked until an explicit
refresh; refreshed state showed `APPROVED`, `POSTED` and `SENT` in turn. The
browser sent each mutation once, PostgreSQL retained one final `SENT` invoice
with the reviewer and transition timestamps, and no page errors occurred.

The recovery message now covers saved invoice status and billing records. The
Angular production build passed at 349.67 kB initial size, with the existing
7.53 kB Commercial Settings stylesheet budget warning. The full solution
suite was not rerun; its latest complete result remains 1001/1001 at
`ead85032`.

## Completed focused slice: Client-scoped invoice billing commands

At code/test commit `8faa79b2`,
`AngularInvoiceBillingScopeJourneyTests.ClientScopedManagerCanUseOwnBillingWorkspaceButCannotMutateSiblingBillingRecords`
passed **1/1** in 28 seconds against isolated PostgreSQL and an API-hosted
Angular browser. A FinanceManager scoped to client A recorded and allocated a
receipt to A's posted invoice and issued a reviewed credit note. Four direct
API attempts to record against client B, allocate B's receipt, cross-allocate
the client's own receipt to B's invoice, or issue a credit note against B all
returned HTTP 403. PostgreSQL confirmed no sibling receipt, allocation or
credit mutation; the browser showed only A's invoice and no page errors.

The Angular production build passed at the unchanged UI source with a 349.67
kB initial bundle and the existing 7.53 kB Commercial Settings stylesheet
budget warning. This focused test-only slice does not change the EF model.
The full solution suite was not rerun; its latest complete result remains
1001/1001 at `ead85032`.

## Completed focused slice: Firm-books role and scope matrix

At code/test commit `fa9c21d2`,
`FirmBooksSeparatesFirmWideFinanceRolesAndDeniesScopedOrExternalIdentities`
passed **1/1** in 42 seconds against isolated PostgreSQL/API hosts. The
firm-wide FinanceManager can prepare expenses but cannot review; the
firm-wide FinanceReviewer can review but cannot prepare. Both can read the
same firm expense. A client-scoped FinanceManager and firm-wide Partner receive
the safe firm-books denial and HTTP 403, while an external client identity is
redirected to the Client portal and receives HTTP 403 from the direct
firm-books API. No denied response contains the expense marker. PostgreSQL
confirms the fixture remained a draft with no journal, review decision or
posting.

The test ran in an isolated worktree because of concurrent uncommitted API
edits in the shared checkout. The full solution suite was not rerun; the latest
complete result remains 1001/1001 at `ead85032`.

## Completed focused slice: Firm-books post outcome reconciliation

At code/test commit `aca91b12`, `AngularFirmBooksJourneyTests` passed **1/1**
in 35 seconds against isolated PostgreSQL/API hosts. One simulated failure
aborted the post request before it reached the API; persisted refresh confirmed
the expense remained `APPROVED`, and the UI required acknowledgement before
allowing a deliberate retry. A second attempt was accepted by the API but its
response was aborted. The UI required another persisted refresh, found the
exact expense `POSTED`, and required acknowledgement; it did not resend the
request. PostgreSQL contains one posting, and the row no longer offers a post
action. The journey also confirms repeated direct post commands return the
same success response. No browser page errors occurred.

The isolated worktree avoided concurrent uncommitted API edits in the shared
checkout. The production Angular build passed with a 349.67 kB initial bundle
and the existing 7.53 kB Commercial Settings stylesheet budget warning. The
complete solution suite was not rerun; its latest result remains 1001/1001 at
`ead85032`.

## Completed focused slice: Firm-ledger blocked-close recovery

At code/test commit `9207a475`,
`FirmLedgerCloseBlockedByUnpostedJournalCanRecoverAfterPosting` passed **1/1**
in 29 seconds against isolated PostgreSQL/API hosts. The Angular close attempt
was refused while a draft journal remained unposted. PostgreSQL confirmed the
period stayed `OPEN` at revision 1 with no close decision. After the manager
submitted and posted the journal and the reviewer approved it, retrying the
close in the same UI session succeeded. The period reached `CLOSED` at
revision 2 with exactly one immutable close decision, the reviewed reason and
the reviewer as actor. No page errors occurred.

The isolated worktree avoided concurrent uncommitted API edits in the shared
checkout. The Angular production build passed at base commit `7e284455` with
the existing Commercial Settings stylesheet budget warning. The complete
solution suite was not rerun; its latest result remains 1001/1001 at
`ead85032`.

## Current focused slice: Invoice lifecycle authorization and recovery

At code commit `7834ddec`, the Angular invoice detail projects current
Application capabilities into its action controls. FinanceManager cannot
approve; an independent FinanceReviewer can approve but cannot post; a
FinanceManager can post only when the approved finance profile matches the
invoice currency, and send remains separately authorized. API authorization
continues to enforce these boundaries for direct requests.

The PostgreSQL-backed API-host Angular lifecycle, invoice scope and billing
recovery cohort passed **3/3**. It verifies denied direct approve/post requests,
independent approval, currency-profile-gated posting, sending, persisted actor
and lifecycle state, repeated post response identity, invoice scope, and the
existing receipt/credit recovery path. Focused Angular finance tests passed
**4/4**; the production Angular build passed with the existing commercial
settings stylesheet budget warning; EF reported no pending model changes.

The focused invoice boundary journey now compares a real foreign-firm invoice
with an unknown ID for the authorized FinanceManager's detail read and
approve/post/send endpoints. Each pair returns the same 403 response; the
foreign invoice remains in review-required state with no approval, post or send
timestamps. Broader FinanceManager/FinanceReviewer and client/firm scope
combinations and remaining uncertain outcomes remain open.

At test commit `de27c966`, the billing workspace journey also checks three
malformed/empty invoice-history cursors. Each returns the same safe HTTP 400
response. Simulated 503 responses while loading older receipts and credit notes
preserve their already-loaded rows and display safe retry messages; retries
then load the remaining history. This API-host browser journey passed **1/1**
in 34 seconds. At test commit `793d29dd`, another journey held a history
response while navigating to a different client's invoice, then released it;
the old invoice's receipt rows did not reappear. That check passed **1/1** in
35 seconds. The remaining FinanceManager/FinanceReviewer and firm/client
scope combinations and uncertain billing outcomes are still open.

The 76 source/action rows remain at one parity
verified, 73 partial, and two unanalyzed; AS-PAR-002 remains partial and the
Blazor retirement gate remains `NOT_READY`. The full solution regression was
not rerun; its last complete result remains recorded at its earlier verified
commit in `status.json`.

## Completed focused slice: Receipt allocation lost-response recovery

At code/test commit `bc276764`, `AngularBillingWorkspaceJourneyTests` passed
**1/1** in 33 seconds against isolated PostgreSQL/API hosts. The API accepted
one 25.00 receipt allocation, then Playwright aborted its response before the
browser received it. Angular displayed “Verify the saved billing state” and
required “Refresh persisted billing state”; the refreshed receipt row showed
25.00 allocated before the unresolved draft could be cleared. PostgreSQL
contained exactly one allocation. The journey also retained lost-response
credit-note reconciliation with exactly one persisted note.

The isolated worktree was used because concurrent uncommitted API edits in the
shared checkout failed compilation. The full solution suite was not rerun; its
latest complete result remains 1001/1001 at `ead85032`.

## Completed focused slice: Client-scoped invoice reviewer boundaries

At code/test commit `84ce3567`, `AngularInvoiceScopeJourneyTests` passed **1/1**
in 42 seconds against isolated PostgreSQL/API hosts. A FinanceReviewer with a
client-scoped grant can read and approve that client's submitted invoice. The
same reviewer receives HTTP 403 when directly approving a sibling-client
invoice; navigating to it shows the safe “not found in the current firm scope”
message without invoice, line or amount markers. PostgreSQL confirms the
sibling invoice remains `DRAFT` with no approver. The existing FinanceManager
client-scope route checks also passed in the journey.

This run used a clean isolated verification worktree because concurrent
uncommitted API changes in the shared checkout failed compilation. The shared
changes were not modified or committed. The complete solution suite was not
rerun; its latest result remains 1001/1001 at `ead85032`.

## Completed focused slice: Invoice-history response after session revocation

At code/test commit `e3301035`, the PostgreSQL-backed API-host Angular
`AngularBillingWorkspaceJourneyTests` passed **1/1** in 34 seconds. The browser
held delivery of a real receipt-history response fetched while the session was
valid, then the user's session epoch was revoked. Invoice refresh and session
revalidation returned 401, the page cleared to Access unavailable, and
delivering the delayed HTTP 200 did not restore invoice or receipt rows. There
were no browser page errors.

The test and Angular production build ran in a clean isolated worktree because
concurrent uncommitted API changes in the shared checkout failed compilation.
The Angular build passed with the existing 7.53 kB Commercial Settings
stylesheet warning against its 4 kB budget. The shared changes were not
modified or included in the test commit. The complete suite was not rerun; its
latest result remains 1001/1001 at `ead85032`.

## Completed focused slice: Firm-ledger close-reason validation

At code/test commit `cfbeed2d`, the PostgreSQL-backed API-host Angular
`AngularFirmLedgerBoundaryJourneyTests` passed **1/1** in 37 seconds. A
whitespace-only reason leaves the Angular Confirm close action disabled. A
direct API request receives the safe HTTP 400 `ledger.invalid` result, and
PostgreSQL confirms the local period remains `OPEN` at revision 1 with no close
decision. The journey also retains the foreign-versus-guessed close-ID denial
and grant-revocation clearing assertions.

State-conflict/provider failure recovery and broader finance role/scope
coverage remain open. The full solution regression was not rerun; its latest
complete result remains 1001/1001 at `ead85032`.

## Completed focused slice: Firm ledger scope and revocation boundaries

At code/test commit `dc43a898`, the PostgreSQL API-host Angular ledger
boundary journey passed **1/1**. It verifies a client-scoped FinanceManager
cannot view the firm-wide ledger, authorized output omits a foreign firm's
period, and a firm-wide reviewer's close requests for a real foreign period
and a random guessed ID receive indistinguishable 403 responses without
changing either firm's period state. Revoking the reviewer grant clears the
visible ledger after reload.

The remaining firm-ledger gaps are state-conflict and failure/recovery
behavior, and the broader role matrix. Remaining firm-books
outcome cases and invoice history/error cases are open. The whole AS-PAR-002
audit and Blazor retirement remain partial/`NOT_READY`.

## Completed focused slice: Firm books authorization and recovery boundaries

At code/test commit `3bff1eb1`, two PostgreSQL-backed API-host Angular journeys
passed **2/2**. They verify malformed and oversized evidence upload rejection,
foreign-firm versus guessed-ID indistinguishable denial for submit/post,
unchanged foreign expense state, session-epoch revocation clearing, and
duplicate post recovery: repeated post requests return the same success
response while PostgreSQL retains one posting. The positive path also keeps
the independent reviewer, persisted rejection reason, same-date expense
submission and balanced/reconciled trial-balance checks.

The broad FinanceManager/FinanceReviewer and scope matrix, remaining invalid
input/content cases, post failure/unknown-outcome reconciliation, invoice
approve/post/send journeys and whole-application AS-PAR-002 review remain open.
Blazor retirement stays `NOT_READY`; no role or production access was widened.
Full suite and EF model-drift checks were not rerun. Current focused evidence
and the built-in browser's expected read-only denial are recorded in
`status.json` and the finance source review. Two unrelated concurrent edits
were left unstaged.

## Completed focused slice: Firm books rejection and journal identity

The Angular firm-expense rejection action now opens a reason dialog, blocks
empty/whitespace input, trims the retained explanation, and uses the maximum
length returned by the Application workspace contract. The service enforces
the same 1,000-character boundary. Finance reviewers still cannot review their
own preparation: the UI hides the action and the API returns 403 for a direct
same-preparer attempt.

The PostgreSQL API-host Angular firm-books journey passed 1/1 at code commit
`788bc218`. It records and submits a documented expense, verifies self-review
denial and an unchanged row, rejects it with a reason verified in PostgreSQL,
then submits a second expense for the same date, has an independent reviewer
approve it, posts it and checks the balanced/reconciled firm trial balance. The
second same-day submission exposed a journal-number collision: the old number
used only the timestamp-bearing first eight characters of a time-ordered GUID.
Expense journal numbers now include the full immutable expense ID.

Focused Angular finance tests passed 5/5, `FirmOperationsTests` passed 3/3,
and the production Angular build passed with the existing commercial-settings
stylesheet warning (7.53 kB against 4 kB). EF reported no pending model
changes. The latest complete PostgreSQL-backed solution checkpoint remains
1001/1001 at `ead85032`; it was not rerun for this slice. The Development
browser identity still has no firm-wide finance assignment, so privileged
expense review was verified in the owned PostgreSQL/Playwright journey rather
than by granting access to the shared Development account. The finance source
review remains partial: malformed/oversized uploads, cross-firm isolation,
retry/revocation matrices, invoices, and overall migration acceptance remain
open. Blazor retirement remains `NOT_READY`.

## Current follow-on slice: Local Blazor rollback-host route smoke

The retained Web rollback/reference host built in Release with zero warnings
and errors. A temporary loopback Test-profile process served the Blazor home at
`/`; its portfolio link opened `/app`, where the unsigned view showed
“Sign-in required” and no actor or client data. The built-in browser observed
both pages, and server logs show Blazor negotiation and a completed SignalR
connection. An unmapped `/app` path returned HTTP 404 in the server log, but
the browser blocked rendering that navigation, so that is recorded as server
response evidence only.

The temporary host was stopped after the smoke. The build included another
agent’s uncommitted consolidation query edit, which the home and portfolio
routes did not exercise and which was excluded from our commits. This is local
Test-profile evidence; it does not establish production rollback, canary,
owner acceptance or Blazor retirement readiness. The migration gate remains
`NOT_READY`.

## Current follow-on slice: Operations authorization-denial recovery control

The Operations page now removes its refresh action after the safe firm-wide
Administrator-denial state, so a scoped Staff user does not see a request
control that can only return the same denial. Refresh remains available after
transient service errors, and a successful explicit retry clears the error.
Server-side Administrator authorization and the nondisclosing no-data response
are unchanged.

`operations.spec.ts` passed 6/6, including the denial/no-refresh and 503/retry
recovery cases. The PostgreSQL-backed API-host Operations browser journey
passed 1/1, including an assertion that Staff sees no refresh action or
operation data. The built-in Development browser confirmed the denial message
without the refresh button. The Angular production build passed with the
existing commercial-settings stylesheet budget warning. No API, Application,
Domain, persistence or EF model changed. The full solution and EF checks were
not rerun; the latest complete-solution checkpoint remains 1001/1001 at
`ead85032`. Migration acceptance and Blazor retirement remain `NOT_READY`.

## Current follow-on slice: Deterministic Global Search lead ordering

The lead query now uses the lead ID as a stable secondary order when names tie,
before applying the six-hit limit. A PostgreSQL regression inserts eight
equal-name matches in reverse ID order, verifies the six lowest IDs are
returned on repeated searches, and confirms the result signals truncation.

`GlobalSearchQueryTests` passed 17/17 in 1m28s and the full API-host
`GlobalSearchJourneyTests` cohort passed 4/4 in 1m33s at code commit
`c5887ee8`. The built-in browser on Development rendered one authorized
“Firm finance” navigation result for a read-only search; the temporary query
and results were cleared. The full solution and EF checks were not rerun; the
complete-solution checkpoint remains 1001/1001 at `ead85032`. Exhaustive
role/scope and other result-kind candidate/ranking matrices remain open; the
Blazor retirement decision remains `NOT_READY`.

## Current follow-on slice: Operations expiry and cross-firm isolation

A focused API-host browser journey exposed concurrent Angular bootstrap/API
requests processing the same expired Administrator grant more than once.
`RoleGrantExpiry` now locks and rechecks the expired grant rows in a database
transaction before writing evidence or advancing the session epoch. The stale
Operations request remains denied and exposes no durable-operation data. A
separate route journey confirms a current-firm Administrator cannot list, retry
or cancel a foreign-firm operation by guessed ID; the denial is nondisclosing
and the foreign record remains unchanged.

The focused Operations browser cohort passed 3/3 in 56s at code commit
`f7bc5e4e`. It covers ordinary Staff denial, expired Administrator revocation
under concurrent shell/API reads, and cross-firm list/retry/cancel isolation.
PostgreSQL-backed `RoleAssignmentTests` and `OperationRecoveryTests` passed
16/16 in 1m20s. Read-back confirms exactly one expiry evidence row and
session-epoch increment, while both pending and foreign-firm operations remain
unchanged. No EF model or schema changed. The full solution and EF checks were
not rerun; the complete-solution checkpoint remains 1001/1001 at `ead85032`.
Other cross-firm capability matrices, ambiguous recovery and broader migration
acceptance remain open; Blazor retirement remains `NOT_READY`.

## Current follow-on slice: Global Search timeout, late-response and session-revocation recovery

Added an API-host browser regression for the Angular Global Search 15-second
timeout. The first synthetic response is held for 16 seconds; the UI shows its
safe retry state, renders no stale result, and succeeds when the user submits
the search again. A second browser case increments the persisted staff session
epoch while a search is pending, then refreshes the protected portfolio. The
API returns 401, protected search content clears, and the delayed response
cannot restore the stale hit.

The full `GlobalSearchJourneyTests` class passed 4/4 in 1m25s at test commit
`fa386c32`. Both delayed-response cases finish without stale content or page
errors; timeout recovery permits an explicit retry. Existing Angular unit
tests cover request cancellation on session invalidation; they were not rerun
in this slice. No runtime, API, Domain, persistence or EF model code changed.
The full solution and EF checks were not rerun; the complete-solution
checkpoint remains 1001/1001 at `ead85032`. Cross-route authorization beyond
these search journeys, broader migration acceptance and Blazor retirement
remain open (`NOT_READY`).

## Current follow-on slice: Portfolio CSV export bound and recoverable limit state

The portfolio export acceptance now pins both sides of its configured
1,000-client limit. The PostgreSQL test succeeds with exactly 1,000 visible
client rows, verifies all rows are exported, then adds one more and confirms
the service refuses the export with `export.limit`. A narrowed search remains
exportable, and the same case retains CSV formula-prefix, quote and multiline
neutralization checks.

`PortfolioWorkspaceTests` and `PortfolioQueryTests` passed 5/5 at test commit
`abb798ea`. Existing coverage also verifies sibling-client count/export
isolation, scoped aggregate projections and refusal when a grant is revoked
during projection.

At test commit `4fc08ea5`, the PostgreSQL API cohort passed 2/2. Its new
1,001-client administrator case verifies HTTP 400, the `export.limit` code,
the safe narrowing message, and a JSON response without CSV attachment. The
Angular API and portfolio component tests passed 19/19 across two files: the
download client preserves that typed refusal, the page clears its busy state,
and a successful explicit retry replaces the error with the completion
message. The component test changes the search to a bounded match, reloads the
workspace, and verifies that the retry request carries the narrowed term.
The API refusal is separately exercised against the PostgreSQL-backed host.

The current built-in browser's Operations screenshot is also confirmed as the
expected denial for the scoped Staff Workspace preview identity: no operation
rows or recovery commands are shown. Existing API/E2E evidence pins 403 and
no-data behavior; no role policy changed.

No production Application, API, Angular or EF model code changed in this
portfolio slice. The full solution and EF check were not rerun; the latest
complete solution result remains 1001/1001 at `ead85032`. Broader portfolio
role/tenant, additional recovery, and overall migration acceptance remain
open; Blazor retirement remains `NOT_READY`.

## Current follow-on slice: Global Search overflow and deterministic ordering

The bounded search queries now have stable ID tie-breakers for equal-ranked
Client, Engagement, PBC and Invoice candidates. A PostgreSQL fixture with 31
valid authorized matches per kind exceeds the 25-row candidate window and
verifies six visible results, `truncated=true`, and the expected stable order.
The configured composite PBC foreign key already prevents mismatched
client/engagement links, so the regression uses only valid persisted states.

`GlobalSearchQueryTests` passed 16/16 at test commit `0fa19701`; the Angular
search browser cohort passed 2/2 at code commit `5357c887`. The built-in
Development browser also showed five authorized synthetic Client, Engagement
and PBC hits in a read-only smoke check. No Angular or EF model code changed.
The full solution, Angular CI/build and EF check were not rerun; the latest
complete solution result remains 1001/1001 at `ead85032`. Exhaustive
role/scope, broader ranking and query/recovery coverage remain open. Migration
acceptance and Blazor retirement remain `NOT_READY`.

## Current follow-on slice: Commercial settings and revocation browser regressions

The commercial-settings browser journey had drifted from the Angular form's
accessible labels and current copy. The visible Required marker remains, while
the native required state now supplies the accessible requirement without
changing the field name. Journey assertions now follow the current version,
threshold, confirmation and empty-state copy. The revocation journey also
allows the page to replace its refresh control during session invalidation;
it still requires the protected workspace to reach Access unavailable.

Both repaired journeys passed together, and the commercial journey cohort
passed. Angular CI, Release build, EF model check and built-in-browser
read-only inspection also passed. The production Angular build retains the
documented commercial-settings stylesheet budget warning. A shared full
solution run reported two E2E failures before these repairs; that full suite
has not been rerun afterward, so it is not recorded as green. The exact counts,
commands and shared-worktree boundary are in `status.json`. Code is committed
as `4a9ebb6a`; overall migration acceptance and Blazor retirement remain
`NOT_READY`.

## Current follow-on slice: Capability-specific Global Search roles and scopes

Added a PostgreSQL-backed matrix covering twelve representative role and
scope combinations. CommercialManager receives Client only; EngagementLeader
receives Client and Engagement; Auditor receives Engagement and PBC; Reviewer,
Accountant, AccountingPreparer and AccountingReviewer receive PBC only;
FinanceManager and FinanceReviewer receive Invoice only under client scope;
RelationshipManager receives Lead only; Senior receives Client, Engagement
and PBC; Administrator lead visibility is also asserted. The focused case
passed 1/1 and the full `GlobalSearchQueryTests` cohort passed 16/16 in 1m19s.
The exhaustive authorization matrix, candidate-window, assistive-technology,
full solution and EF checks remain open. Evidence is at `df8a62f5`; migration
and Blazor retirement remain `NOT_READY`.

## Current follow-on slice: Legacy search category ordering parity

The PostgreSQL-backed Angular browser journey captures the response for a
cross-kind `pbc test` search and asserts the category sequence Client,
Engagement, PBC request. This matches the legacy Blazor component, which grouped
hits by kind in first-occurrence order rather than assigning a relevance
score. `GlobalSearchJourneyTests` passed 2/2 in 47s. Candidate-window cases,
full role/scope coverage and assistive-technology acceptance remain open;
Blazor retirement remains `NOT_READY`. Evidence is at `8c220032`.

## Current follow-on slice: Search timeout and session invalidation

Added Angular component assertions that a 15-second search timeout shows the
safe retry message without stale results, and that session invalidation
cancels an in-flight request and clears prior results. The focused search
suite passed 4/4; full Angular CI passed 495/495 across 95 files. The Angular
production build passed with a warning: `settings.scss` is 7.53 kB against its
4.00 kB component-style budget; the initial bundle is 349.67 kB. These tests
cover component behavior; candidate-window cases, exhaustive role/scope,
browser assistive-technology acceptance, the full solution and EF drift remain
open. Evidence is at `477a953f`; migration and Blazor retirement remain
`NOT_READY`.

## Current follow-on slice: Per-kind search caps across all result types

Added `ClientEngagementPbcAndInvoiceCapsSignalWhenMoreThanSixMatchesExist`.
The PostgreSQL-backed test seeds seven authorized matches each for Client,
Engagement, PBC request and Invoice, then verifies six results per kind and
`truncated=true`. With the existing Lead, Technical Library and Page cases,
query cap/truncation evidence now covers all seven result types.
`GlobalSearchQueryTests` passed 15/15 in 1m16s. Candidate-window exhaustion,
cross-kind ranking, exhaustive role/scope, search timeout/in-flight
invalidation, Angular CI/build, the full solution and EF drift remain open.
This evidence is at `9c4e24e3`; overall migration and Blazor retirement remain
`NOT_READY`.

## Current follow-on slice: Page-only search truncation guidance

Extended the PostgreSQL-backed Global Search browser journey to verify the
page-only overflow path. It checks that the API returns six `Page` hits with
`truncated=true`, the Angular view renders six links, and the refinement hint
is visible. `GlobalSearchJourneyTests` passed 2/2 in 46s; test commit
`0273cbd5` is pushed. This closes the page-only hint case; other result-kind
caps/ranking and exhaustive role/scope remain open. Timeout and in-flight
session invalidation remain open, and full migration/retirement acceptance is
still `NOT_READY`.

## Current follow-on slice: Global Search safe failure and retry

Extended the PostgreSQL-backed Angular browser journey with a synthetic 503
for a new search term. The page displays the safe retry message, reveals no
previous result links or server diagnostic, and succeeds when the same search
is retried. It also rejects a malformed 200 response without rendering the
unsafe link, then clears the protected search workspace after a synthetic 401.
`GlobalSearchJourneyTests` passed 2/2 in 47s; test commits `531488df` and
`0407e696` are pushed. The Operations screenshot was also rechecked in the
built-in browser: its signed-in Workspace preview identity has no firm-wide
Administrator grant, and the page exposes no operation rows or commands.
Timeout and in-flight session invalidation remain open, along with broader
migration and retirement acceptance; Blazor remains `NOT_READY`.

## Current follow-on slice: Angular search truncation hint

Expanded the Global Search browser fixture to seven published library entries.
The Staff UI displays six links and the “Refine your search for more specific
results” hint, then opens the selected exact library route. The full
`GlobalSearchJourneyTests` cohort passed 2/2 in 47s; the UI assertion is pushed
as `4b019b0c`. The PostgreSQL query tests separately prove page and library
`truncated=true` signaling. Ranking/candidate behavior for other result kinds,
exhaustive role/scope, errors/recovery, accessibility and full migration
acceptance remain open.

## Current follow-on slice: Search cap and truncation signaling

Fixed Global Search so technical-library and page result caps report
`truncated=true` when more than six authorized/available hits exist. The
PostgreSQL test seeds seven published library items and verifies six returned
with truncation; it also verifies the page catalogue's six-hit boundary. The
full `GlobalSearchQueryTests` cohort passed 14/14 in 1m08s, and
`GlobalSearchJourneyTests` passed 2/2 in 48s. Pushed as `a67e6a06`.
Candidate/ranking limits and role/scope matrices for other result kinds, broad
query/error recovery, accessibility and full migration acceptance remain
open. Blazor retirement remains `NOT_READY`.

## Current follow-on slice: Global Search linked destinations

Extended the Angular Global Search browser journey to open both a published
technical-library result and a supported page result. The scoped Staff user
opens the exact library entry and the Practice time page, and search results
clear after each navigation. The full `GlobalSearchJourneyTests` cohort passed
2/2 in 48s; this extension is pushed as `c4eb8c57`. PostgreSQL tests separately
cover Manager-only audience, drafts and other-firm library matches. This
improves destination evidence only; ranking/candidate boundaries, exhaustive
role/scope, other error/recovery and accessibility coverage remain open.
Blazor retirement remains `NOT_READY`.

## Current follow-on slice: Global Search opens technical-library entries

Extended the Angular Global Search browser journey with a real published
`ALL_STAFF` technical-library fixture. A scoped Staff user sees the result,
opens its exact Angular library route, and the shell clears search results on
navigation. `GlobalSearchJourneyTests` passed 2/2 in 45s; the new journey is
pushed as `59c8abc3`. Combined with the PostgreSQL audience/publication/firm
boundary case, this verifies the Staff search-to-library path and selected
role boundaries. Ranking/candidate behavior, exhaustive role/scope, broad
recovery and accessibility evidence remain open; Blazor retirement remains
`NOT_READY`.

## Current follow-on slice: Operations administrator-denial clarity

Rechecked `/ui/app/operations` in the built-in browser. The signed-in Staff
preview shows the explicit firm-wide AuditSphere Administrator requirement
and the safe next step to switch accounts or ask an administrator; no operation
rows or commands are exposed. Added an E2E assertion for that exact alert; the
authorized recovery plus Staff-denial browser journey passed 1/1 in 32s and is
pushed as `f8c702f1`. No authorization behavior changed. Expired/cross-firm
grants, ambiguous/concurrent recovery and quarantine paths remain open.

## Current follow-on slice: Unauthenticated root sign-in handoff

Extended `AngularRouteAndShellMigrationSweepTests.RootRedirectRespectsPreviewAndCanonicalAngularOwnership` to begin without a cookie in both route modes. Each case verifies the protected Angular shell shows its sign-in prompt, preserves the matching return destination (`/app` or `/ui/app`), and reaches Portfolio after the development identity signs in. The browser cohort passed 2/2 in 39s and is pushed as `be9d736d`. This closes the root-route unauthenticated journey only; fallback-host behavior remains open. No authorization policy changed, and Blazor retirement remains `NOT_READY`.

## Current follow-on slice: Global Search technical-library audience boundary

Added a PostgreSQL-backed `GlobalSearchQueryTests` case for published technical
library results. It verifies a client-scoped Staff grant returns only published
`ALL_STAFF` guidance; a Manager also sees published `PARTNERS_MANAGERS`
guidance; neither sees drafts or a same-term record from another firm. The new
case passed 1/1 in 21s and the full `GlobalSearchQueryTests` cohort passed
13/13 in 1m05s. The test is pushed as `c5350bc2`. This is the query-level
audience/publication/firm boundary evidence; the later `59c8abc3` browser
journey verifies the Staff search-to-library route. Ranking/candidate-boundary,
exhaustive role/scope, assistive-technology, complete migration and retirement
acceptance remain open. The other agent's uncommitted consolidation source and
inventory edits were excluded from the commit. Blazor retirement remains
`NOT_READY`.

## Current follow-on slice: Canonical root redirect evidence

Reran `AngularRouteAndShellMigrationSweepTests.ParameterlessRoutesRenderAndPreserveRoleSpecificShells`; 1/1 passed in 37s. Its parameterless-route matrix directly visits `/` in canonical API-route mode and confirms it reaches the Portfolio heading with Practice navigation current. The additional two-mode journey below covers preview root ownership. The unauthenticated root sign-in handoff is now covered separately; fallback-host behavior remains open. These are verification and ledger corrections, not route changes. Blazor retirement remains `NOT_READY`.

Added `RootRedirectRespectsPreviewAndCanonicalAngularOwnership`; its two
PostgreSQL-backed browser cases passed 2/2 in 41s. They verify the final path
and Portfolio heading after `/` redirects to `/app` in canonical mode and
`/ui/app` in preview mode. Fallback-host behavior remains open. This test-only
change is pushed as `c3a9ad21`.

## Current follow-on slice: Client contact creation recovery evidence

Reran the existing PostgreSQL-backed
`AngularClientContactCreationJourneyTests` cohort on canonical and `/ui` routes;
2/2 passed in 43s. The journey covers draft restoration, reviewed primary
contact replacement, a dropped create response followed by receipt
reconciliation without duplicate creation, and protected-content clearing
after the Manager grant is revoked. The ClientDetail source/action row remains
partial because complete UI field validation and stale-generation conflict
rendering, full role and cross-firm matrices, and assistive-technology
acceptance are still open. The PostgreSQL-backed
`ClientContactCreationTests` also passed 4/4 in 33s, covering
concurrent idempotency, immutable receipts, changed-request conflict, invalid
field samples, stale-generation refusal, foreign-firm/client/role denial, and
rollback after authority revocation during publication. These were existing
committed tests; this turn records fresh verification only. Complete UI
validation/conflict rendering, broader role/field-redaction and cross-firm
HTTP matrices, and assistive-technology acceptance remain open. Blazor
retirement remains `NOT_READY`.

## Current follow-on slice: Global Search bounds and candidate caps

Added API-host coverage for the search endpoint's one-character empty result
and safe `request.invalid` response above the 100-character limit. Added a
PostgreSQL case proving lead results stop at six and set `truncated=true` when
there are more matches. `GlobalSearchJourneyTests` passed 2/2 and
`GlobalSearchQueryTests` passed 12/12. This slice changed tests only. The
shared Release build compiled the other agent's concurrent uncommitted
`ConsolidationOverviewQuery.cs` edit, which these search tests do not exercise
and which was excluded from the commit. Full solution, Angular and EF checks
were not rerun; remaining indexed result kinds, complete role/scope, error and
assistive-technology coverage stay open. Pushed as `c716e2fa`; retirement is
still `NOT_READY`.

## Current follow-on slice: Portfolio aggregate scope parity

Extended `AngularPortfolioJourneyTests.ScopedSummaryRecordsCsvAndReturnFilters`
to inspect `GET /api/ui/portfolio/workspace` directly while the sibling client
contains its own fixture data. On canonical and `/ui` routes, the API returns
only the assigned client, counts one authorized client and engagement, reports
the 25 ready candidates and one issued release for that client, and omits the
sibling marker. Both browser journeys passed (2/2). The slice changed tests
only; the shared build also compiled the other agent's uncommitted
`ConsolidationOverviewQuery.cs`, which this portfolio test does not exercise
and which was excluded from the commit. Full solution and EF drift checks were
not rerun. Portfolio source parity remains partial; Blazor retirement remains
`NOT_READY`. The test is pushed as `3511f903`.

## Current follow-on slice: Global Search invoice scope parity

Extended the PostgreSQL-backed `GlobalSearchQueryTests` cohort to seed invoices
for an assigned client, a sibling client in the same firm, and a client in a
different firm. FinanceManager scoped to the assigned client sees only its
invoice; ordinary Staff sees no invoice hits; firm-wide FinanceReviewer sees
the two in-firm invoices; the foreign-firm invoice never appears. The focused
case passed 1/1 and the full query class passed 11/11. This slice changed tests
only. The shared Release build also compiled the other agent's concurrent
uncommitted `ConsolidationOverviewQuery.cs` edit; these search tests do not
exercise that code and it was excluded from this commit. The full solution
suite and EF drift check were not rerun. Search result-kind and exhaustive
role/scope coverage remain partial; Blazor retirement remains `NOT_READY`.
The test is pushed as `672cbfbc`.

## Current follow-on slice: Global Search API authorization parity

Added a direct API-host PostgreSQL browser journey for `GET /api/ui/search`.
It verifies an assigned Staff identity can find its client, while exact
sibling-client and foreign-firm markers return no hits and no `truncated`
count. Client identities receive 403; after client-grant revocation the API
returns no results; after session-epoch change the same cookie receives 401.
The direct API journey passed 1/1, the full Global Search browser class passed
2/2, and `GlobalSearchQueryTests` passed 10/10. The Global Search source row
remains `PARTIAL`; all result-kind, full role/scope, query/error and
assistive-technology matrices remain open. No EF model changed. The full
solution regression was not rerun; the latest remains 1001/1001 at `ead85032`.
The test is pushed to master as `6c0b0f49`; Blazor retirement remains
`NOT_READY`. See the [shell, search, portfolio and profiles source
review](../migration/auditsphere-migration-blazor-shell-portfolio-source-review.md).

## Current follow-on slice: Acceptance checklist workspace and role parity

Added a PostgreSQL-backed API-host browser journey for the Angular acceptance
checklist. It verifies current workspace and progress state, pending specialist
clearance, Staff editing/request controls, Manager review controls without
Partner decision access, refresh confirmation that keeps unsaved input, and
confirmed discard restoring persisted answer/evidence. Revoking the Staff
client grant then refreshing clears the client profile, question and evidence.
The focused acceptance cohort passed 7/7; Angular CI passed 493/493 across 95
files; the production build passed with the existing Commercial Settings style
warning (7.53 kB against 4 kB). Built-in browser inspection of the supplied
Operations page confirmed the firm-wide Administrator denial and no operation
rows. The checklist source row remains `PARTIAL`; full role/scope, validation,
stale/error/recovery, assistive-technology, production/canary, live Microsoft
and owner acceptance remain open. No EF model changed; the pending-model check
was not rerun. The latest full solution regression remains 1001/1001 at
`ead85032`; Blazor retirement remains `NOT_READY`. See the [acceptance
checklist source review](../migration/auditsphere-migration-blazor-acceptance-checklist-source-review.md).

## Current follow-on slice: Release issue and lost-response recovery

Added the missing Angular journey for a verified release candidate. The
PostgreSQL-backed API-host test uses a synthetic read-back checkpoint and
current protection attestation, accepts the issue request, then drops the
response. Angular reloads the candidate, confirms its persisted `ISSUED`
state, and clears the unknown-outcome notice without replaying the command.
The journey verified one issue POST, key clearing before dispatch, no key in
browser storage, one immutable release event and one durable operation. The
Release cohort passed 2/2, the records decoder tests passed 5/5, and the
production Angular build passed with the existing Commercial Settings
stylesheet warning (7.53 kB against a 4 kB budget). The built-in browser
checked a guessed Release ID and showed only the generic unavailable state.
The Release and Records Archive source rows remain partial. The full solution
regression and EF drift check were not rerun; the latest full result remains
1001/1001 at `ead85032`. Blazor retirement remains `NOT_READY`.

## Current follow-on slice: Operations access-denial clarity

The built-in browser showed a signed-in staff user on the Operations page with
an access message that incorrectly suggested signing in again. Updated the
message to explain the firm-wide AuditSphere Administrator requirement and
offer the appropriate next step. The API authorization remains unchanged. The
focused Operations Angular suite passed 4/4, the production Angular build
passed with the existing Commercial Settings stylesheet warning (7.53 kB
against a 4 kB budget), and the built-in browser confirmed the message while
showing no operation records. The full solution regression and EF drift check
were not rerun; Blazor retirement remains `NOT_READY`.

## Current follow-on slice: Shared confirmation source/action parity

Reviewed the two callers of the shared Blazor confirmation dialog against the
Angular role-revocation and lead-qualification workflows. The Angular revoke
dialog now identifies the exact user, Microsoft identity, role and scope before
submission; lead qualification displays the immutable lead ID and makes clear
that it grants neither professional acceptance nor client access. The focused
PostgreSQL-backed API-host browser cohort passed 2/2, verifying both flows,
persisted changes and session invalidation. Angular CI passed 491/491 across
95 files. The production build passed with the existing 7.53 kB Commercial
Settings stylesheet warning against a 4 kB warning budget.

Both callers remain partial; the 76-row source/action inventory now
has 73 partial reviews, two unanalyzed rows, one parity-verified row, and 28
supporting files open. Complete authorization and unknown-outcome matrices,
human assistive-technology/locale acceptance, production/canary evidence, live
Microsoft gates, and separate owner acceptance remain open. The built-in
browser reproduced the Operations denial for the `Workspace preview` identity;
no operation data was shown and no privilege was changed. The full solution
regression and EF model-drift check were not rerun; the latest complete result
remains 1001/1001 at `ead85032`. Blazor retirement remains `NOT_READY`. See the
[shared confirmation source review](../migration/auditsphere-migration-blazor-confirm-dialog-source-review.md).

## Current follow-on slice: Risk-routing source/action parity

Compared the pinned `RiskRoutingPanel.razor` source with the Angular audit-plan
routing section, scoped API endpoints, `RiskBandService`, and existing
professional-review safeguards. Angular now requires nonblank rationale before
assessing a band and a nonblank note before recording Partner clearance. The
PostgreSQL-backed API-host browser journey passed 1/1, creating a risk through
Angular, assessing it as amber, assigning a qualified engagement owner, and
recording an independent Partner clearance on a separate red risk. Angular CI
passed 491/491 across 95 files; the production build passed with the existing
Commercial Settings stylesheet warning (7.53 kB against a 4 kB budget).

The risk-routing source/action row is now `PARTIAL_REVIEWED`, not parity
complete. Full role/scope/expiry/cross-firm coverage, stale and unknown-result
recovery, and human assistive-technology/locale acceptance remain open. The
register now has 72 partial reviews, three unanalyzed rows, one
parity-verified row, and 28 supporting files open. The full solution regression
and EF model-drift check were not rerun; the latest complete result remains
1001/1001 at `ead85032`. See the [audit planning and fieldwork source
review](../migration/auditsphere-migration-blazor-audit-planning-fieldwork-source-review.md).

## Current follow-on slice: Client Portal home source/action parity

Compared the pinned `ClientPortal.razor` home page with the Angular portal
home, request, shared-document and package components and the scoped API and
Application projections. Restored 10/25/50 request and package pagination,
added strict API page-size validation, and matched the legacy pending-onboarding
state so it shows no empty workbench sections before a client engagement opens.
Focused Angular tests passed 2/2; full Angular CI passed 491/491 across 95
files; the production build passed with the existing Commercial Settings
stylesheet warning. PostgreSQL-backed domain and API portal cohorts passed 3/3
and 2/2. The API-host client browser journey passed 1/1 for first sign-in,
delegation/revocation, conversation, upload staging, mobile width, guessed-ID
denial and session revocation. The built-in browser's staff preview was
redirected from `/ui/portal` to the staff portfolio without disclosing client
data.

The Client Portal home source row is now `PARTIAL`, not parity complete:
document acknowledgement/comment recovery, signed-representation failures,
bundle authorization, complete role/scope matrices and human assistive-
technology acceptance remain open. The source/action register now has 71
partial reviews, four unanalyzed rows and one parity-verified row, with 28
supporting files open. The complete solution regression and EF model-drift
check were not rerun; the latest full result remains 1001/1001 at `ead85032`.
See the [Client Portal Home source review](../migration/auditsphere-migration-blazor-client-portal-source-review.md).

## Current follow-on slice: Operations recovery source/action parity

Compared the legacy Operations recovery page with the Angular workbench, API
contracts and Application authorization. Restored the legacy 10/25/50 row
choices and added explicit table semantics. The Angular unit suite passed 3/3;
full Angular CI passed 489/489 across 94 files; the production build passed
with the existing Commercial Settings stylesheet budget warning. Two focused
PostgreSQL-backed API-host browser journeys passed, covering firm-admin
cancel/re-arm with persisted evidence, staff denial, redaction, revocation,
responsive width and keyboard focus. The built-in Development browser
confirmed the current identity is denied without operation disclosure.

The Operations row is recorded `PARTIAL`: quarantine and concurrent/unknown
outcome paths, the complete role/expiry/cross-firm matrix, and human
assistive-technology acceptance remain open. The source/action register now
has 70 partial reviews, five unanalyzed rows and one parity-verified row, with
28 supporting files open. The full solution regression and EF model-drift
check were not rerun; the latest complete result remains 1001/1001 at
`ead85032`. AS-PAR-002 and Blazor retirement remain partial/`NOT_READY`; see
the [Operations Recovery source review](../migration/auditsphere-migration-blazor-operations-recovery-source-review.md).

## Current follow-on slice: Statement Drilldown and Trial Balance Intake

Reviewed the two legacy accounting pages against their pinned source hashes and
mapped them through Angular, API endpoints and Application services. The
focused Angular statement/intake unit cohort passed 28/28. PostgreSQL-backed
API-host journeys passed 3/3, including invalid-period rejection, reviewed
multi-period upload, owned worker validation, receipt recovery, source export,
lost-response reconciliation, revocation clearing, and statement-line to
procedure navigation. The built-in browser showed generic scope-denied states
for guessed engagement IDs on both current Angular routes.

Both rows remain `PARTIAL`. Complete per-action role/scope, expiry and
cross-firm matrices, the full file-format/size/error and mapping/currency
outcome crosswalks, and human screen-reader/locale acceptance remain open. The
inventory now has six unanalyzed rows, 69 partial rows and one parity-verified
row; 28 supporting files remain open. The whole-solution regression and EF
model-drift check were not rerun. AS-PAR-002 and Blazor retirement remain
partial/`NOT_READY`; see the [Statement and Intake source review](../migration/auditsphere-migration-blazor-statement-intake-source-review.md).

## Current follow-on slice: Records Archive and Release reviews

Reviewed the Records Archive and Release Razor pages against their pinned
source hashes and mapped both detail routes through Angular, API projections,
and Application authorization. Focused PostgreSQL browser journeys passed
1/1 for Archive scope/detail behavior and 1/1 for expired-protection Release
gates, scope isolation, and stale-content clearing. Read-only built-in-browser
checks showed generic unavailable states for guessed archive and release IDs.

Both rows remain `PARTIAL`. The verified-candidate Angular issue success and
uncertain-result path, full role/scope and error matrices, archive empty and
large-manifest variants, and human accessibility/locale acceptance remain
open. The source/action inventory now has 8 unanalyzed rows, 67 partial rows,
and one parity-verified row. Exact evidence and gaps are in the
[records source review](../migration/auditsphere-migration-blazor-records-source-review.md).

## Current follow-on slice: ReviewPoint source/action review

Reviewed the ReviewPoint route against its pinned Razor hash and mapped its
scoped detail and clear/reopen actions through Angular, the API, and
Application services. Focused PostgreSQL verification passed 1/1 API-host
browser journey and 1/1 Domain transaction test. The browser journey confirms
clear/reopen persistence, sibling and guessed-ID denial, route restoration,
keyboard focus, responsive widths, and denial plus content clearing after
grant revocation. The built-in browser showed the generic unavailable state
for a guessed Development ID.

The row is recorded `PARTIAL`: a full role/scope and expired-grant matrix,
network/stale-command recovery, and human assistive-technology/locale
acceptance remain open. The inventory now has 10 unanalyzed rows, 65 partial
rows, and one parity-verified row. Full-suite regression, EF drift, production
acceptance, and Blazor retirement are not closed by this slice; see the
[ReviewPoint source review](../migration/auditsphere-migration-blazor-review-point-source-review.md).

## Current follow-on slice: Workpaper source/action review

Reviewed the Workpaper Razor source against its pinned discovery hash and traced
the Angular/API/Application replacement through draft save, discard, submit,
scope checks, and immutable submission history. The focused PostgreSQL-backed
API-host browser cohort passed 5/5, including save-outcome recovery, client and
engagement denial, revocation clearing, and fieldwork linkage. The Workpaper
source/action row is now `PARTIAL`; 11 source/action rows remain unanalyzed, 64
are partial, and one is parity verified. The evidence and concrete gaps are in
the [Workpaper source review](../migration/auditsphere-migration-blazor-workpaper-source-review.md).

Full role/scope and error/recovery matrices, a dedicated browser discard
journey, and human assistive-technology/locale acceptance remain open. The
full solution regression and EF drift check were not rerun; latest full suite
remains 1001/1001 at `ead85032`. The unrelated consolidation and inventory
edits in the shared worktree were excluded. AS-PAR-002 remains partial and
Blazor retirement remains `NOT_READY`.

## Current follow-on slice: reduce the Angular shell's initial bundle

The root shell no longer imports the Material toolbar and button modules. The
compact workspace-navigation Material dialog is loaded only when requested, and
the route deactivation guards are split from their dialog components. This keeps
the existing keyboard, focus, resize, route-change and session-revocation
behavior while removing unused Material/CDK dialog and button chunks from the
initial dependency graph.

The production Angular build now reports a 349.67 kB raw initial bundle (94.01
kB estimated transfer), down from 506.60 kB and below the 500 kB warning
budget. All 488 Angular unit tests and three focused PostgreSQL-backed
Playwright cases passed. The built-in browser loaded the API-host Commercial
Settings page read-only after the rebuild. The 7.53 kB Commercial Settings
component stylesheet still exceeds its 4 kB component-style warning budget;
this keeps US-043 partial. No .NET or EF model code changed, and the full
solution regression and pending-model check were not rerun. This slice does not
change the migration's `NOT_READY` retirement decision: source/action reviews,
production/canary, human accessibility, live Microsoft, and owner acceptance
gates remain open. Exact evidence and commands are recorded in
[`status.json`](status.json).

## Current follow-on slice: Angular workpaper save-outcome recovery

The Workpaper editor now treats a timeout or server error after a draft save as
an unknown outcome. It retains the exact request identity and content, blocks
editing/submission/navigation, and asks the practitioner to check the protected
persisted draft. An exact match confirms the save without another write; an
unchanged draft revision enables only a deliberate retry of the same idempotent
request. A changed target fails closed. If access is revoked during
reconciliation, the editor clears protected content and reloads through the
scope-checked resource. When Angular reuses this editor for another workpaper,
prior success/error feedback is cleared; same-workpaper refreshes retain it.

Focused Angular tests, the complete Angular CI suite, the production build, and
a PostgreSQL-backed API-host Playwright journey passed. The browser journey
commits the draft while deliberately dropping the response, verifies exact
read-back without a duplicate POST, then submits once and checks persisted
draft/submission evidence. The built-in browser reloaded the route read-only;
the unassigned Development identity saw the generic unavailable state for a
guessed ID. The production build retains the recorded bundle and
commercial-settings style budget warnings. No EF model changed; neither the
pending-model check nor full-solution regression was rerun for this UI/API-host
test slice. Exact commands, counts, commit and boundaries are recorded in
[`status.json`](status.json). This closes only the Workpaper uncertain-save
case: broader AS-PAR-002 authorization/retry review remains partial, and
Blazor retirement stays `NOT_READY`.

## Current follow-on slice: firm finance source review

The Finance ledger, FirmBooks and InvoiceDetail Razor sources match their
pinned discovery hashes and are mapped to the Angular finance components, API
endpoints and Application services. A gap in existing evidence was corrected:
the prior expense-to-trial-balance journey still ran through the legacy host,
so a dedicated API-host Angular journey now covers source-document capture,
submission, independent FinanceReviewer approval, ledger posting, persisted
identities and balanced/reconciled trial-balance output. The new journey passed
1/1. The existing focused finance/invoice/accessibility cohort also passed
6/6; its legacy FirmOperations test is explicitly not treated as Angular
evidence.

The Angular production build passed with the known bundle and component-style
warnings. In the built-in Development browser, the current identity had no
firm-wide finance grant; Firm ledger and Firm books showed access denied with
no finance data. No business action was submitted. All three source rows stay
`PARTIAL`: complete role/scope and cross-firm isolation, expense rejection and
validation, close/post recovery, and invoice approve/post/send coverage remain
open. The full solution regression and EF model-drift check were not rerun;
the latest complete suite remains 1001/1001 at `ead85032`. Exact test commands,
source hashes, shared-worktree boundaries, browser observation and open gaps
are in [`status.json`](status.json) and the [finance source review](../migration/auditsphere-migration-blazor-finance-source-review.md).
AS-PAR-002 remains partial and Blazor retirement remains `NOT_READY`.

## Current follow-on slice: practice operations source review

Practice time, practice analytics, project progress and the technical library
were checked against their pinned discovery hashes and mapped to native
Angular routes and API/Application owners. The focused PostgreSQL-backed
Release browser cohort passed 5/5: scoped time visibility and independent
approval, library publish/search, analytics definitions, project-progress
filtering/denial, and the 20-route accessibility contract. The built-in
browser rendered the four Development pages read-only; no business command
was submitted.

All four source rows remain `PARTIAL`. The library UI's create/version forms
and audience isolation, populated analytics formulas and edge cases, the
complete time role/retry/revocation matrix, and malformed/stale progress
snapshot paths still need direct coverage. The full solution regression and
EF pending-model check were not rerun; the latest complete regression remains
1001/1001 at `ead85032`. Exact hashes, command and counts, browser boundaries,
and shared-worktree notes are recorded in [`status.json`](status.json) and the
linked source review. AS-PAR-002 remains partial and Blazor retirement remains
`NOT_READY`.

## Commercial Settings form UI repair

The commercial settings form now uses a scoped, responsive grid with aligned
labels and controls, clearer field guidance, grouped sections and a separated
review/action area. The Angular component bindings and business behavior were
preserved. The production Angular build passed with its existing initial
bundle warning (506.60 kB, 6.60 kB above the 500 kB warning budget); the
focused settings component tests passed 6/6. The built-in browser rendered the
Development Commercial Settings page read-only; no business command was
submitted. No API, Application, Domain or EF model code changed. This UI-only
repair does not change migration parity or retirement acceptance.

## Current follow-on slice: resource planning, budget and staffing source review

The resource planning page and its engagement budget and staffing panels were
checked against the pinned discovery snapshot; all three source hashes match.
The native ownership mapping covers the Angular resource workbench and
engagement planning UI, API endpoints, and Application queries/workspaces for
resource edits, reviewed resource commands, budget preparation and approval,
and staffing changes.

The focused PostgreSQL-backed Release browser cohort passed 12/12 with no
failures or skips. It covers canonical and `/ui` routes for resource form and
stale-review behavior, exact budget review and receipt recovery, independent
approval, staffing add/revoke recovery, session-epoch invalidation and assent
clearing. The built-in browser rendered the Development resource planning page
read-only; no business command was submitted.

All three source rows remain `PARTIAL`. Complete role/scope, firm and guessed-ID
isolation, all validation/failure/recovery branches, live SharePoint membership
reconciliation and assistive-technology parity remain open. The full solution
regression and EF pending-model check were not rerun for this slice; the latest
complete regression remains 1001/1001 at the separate `ead85032` checkpoint.
Source hashes, focused test command and counts, browser boundary and open gaps
are recorded in [`status.json`](status.json) and the linked source review.
Blazor retirement and AS-PAR-002 remain `NOT_READY`/partial.

## Current follow-on slice: commercial workflow source review

Six legacy commercial sources were checked against the pinned discovery
hashes and mapped to the Angular leads, proposal, quotation, document,
fee-agreement, commercial-settings and reviewed conversion flows, their API
endpoints, and Application owners. The PostgreSQL-backed Playwright journeys
cover commercial create/revision recovery, quotation and document work,
fee-agreement milestones and authority, settings drafts, reviewed conversion,
plus independent proposal review, status-only send, and accepted/declined
client responses.

All six sources remain `PARTIAL`. Existing focused evidence does not establish
the full role/scope and isolation matrices, every validation and failure/retry
branch, external delivery, or assistive-technology parity. The built-in
browser rendered Leads and Commercial Settings read-only. The complete
solution regression, Angular unit/build checks and EF model-drift check were
not rerun for this slice. Evidence and volatile counts are recorded in
[`status.json`](status.json) and the linked source review; owner, production,
external and accessibility acceptance remain open, so Blazor retirement
stays `NOT_READY`.

## Current follow-on slice: shell, search, portfolio and profile source review

Six legacy source/action rows were checked against the pinned discovery hashes
and mapped to the Angular shared shell, global-search query, portfolio, client
profile and engagement profile, their API endpoints and Application queries.
The focused PostgreSQL-backed Release browser cohort passed for the global
search, responsive shell navigation, portfolio scope/export, client profile
paging/revocation and engagement profile authority cases. The built-in browser
rendered the Development portfolio, client and engagement routes read-only.

All six rows are recorded `PARTIAL`; focused journeys do not establish every
role, field, denial, query/result type, failure/recovery path, cross-firm or
guessed-ID response, or assistive-technology outcome. The source review,
exact hashes, test command, counts and browser boundary are recorded in
[`status.json`](status.json) and the linked migration review. No full solution
regression or EF model-drift check was run for this slice. Production/canary,
human, external and owner acceptance gates remain open, so Blazor retirement
stays `NOT_READY`.

## Current follow-on slice: completion and file records source/action review

The three completion-family Razor sources were checked against the pinned
discovery hashes and mapped to the native Angular completion workspace, its
API endpoints, and the Application completion, freeze, and activity services.
The mapping covers gates and release-candidate preparation; confirmations,
SRM/Partner clearance, opinion and reports; client review/signing and final
bundle; plus file freeze, amendments, locks and activity history. All three
rows remain `PARTIAL`; the review found no missing primary Angular/API owner.

Focused PostgreSQL-backed domain and API-host browser tests passed, including
the deliverable/client-review path, sibling-engagement denial and stale-route
clearing. A further Angular browser journey now covers the amendment and
document-lock lifecycle: requester self-approval denial, independent Partner
approval, lock contention, unauthorized release, authorized Partner release,
closure/refreeze, persisted evidence and the filtered activity trail. The
read-only built-in browser rendered the local Development completion workspace
without submitting a command. Exact commands and results are recorded in
[`status.json`](status.json). Cross-firm/guessed-ID, complete role/error/recovery,
revocation-during-workflow, accessibility and assistive-technology coverage
remain open. No EF model drift check or whole-solution regression was run for
this slice; the complete migration gate remains
`NOT_READY`.

## Current follow-on slice: blocked engagement creation source/action review

The third acceptance source row is hash-checked and mapped to the native client
engagement-creation route, its API contracts, and the Application services. It
creates only a reviewed, blocked draft; exact duplicates route to the existing
engagement, and uncertain responses reconcile through actor-owned receipts.
Editable fields can be saved in the current tab without preserving review
assent. Partner activation remains separate.

Five PostgreSQL-backed domain review/recovery tests passed, as did the
canonical and `/ui` API-host browser journey (2/2). Angular CI passed 483/483,
and the production build passed with the existing 6.60 kB initial-bundle
warning-budget overage. The built-in browser showed the generic unavailable
state for the unassigned Development identity. Cross-firm/guessed-ID and full
validation/accessibility parity remain open; EF drift and whole-solution tests
were not rerun. Inventory and exact evidence are in [`status.json`](status.json);
Blazor retirement stays `NOT_READY`.

## Previous follow-on slice: engagement activation source/action parity review

The next acceptance source row is now hash-checked and mapped to the Angular
activation route, API, and Application gate. The current UI requires a Partner
to review the exact accepted client/service/generation and active holds, preview
the request, explicitly assent, and reconcile an immutable receipt after an
uncertain response. The Application lifecycle still enforces the current
unconditional same-route acceptance and hold rules transactionally.

The activation domain gate passed 1/1, the PostgreSQL-backed Angular browser
journey passed 2/2 in both route ownership modes, Angular CI passed 483/483,
and the production build passed with the existing 6.60 kB initial-bundle
warning-budget overage. The built-in browser returned the generic unavailable
state for the unassigned Development identity. The row is PARTIAL because the
full cross-firm/guessed-ID and presentation/accessibility matrix remains open.
No EF drift check or full solution suite was rerun. The Blazor rollback host
remains `NOT_READY` for retirement; exact evidence and counts are in
[`status.json`](status.json).

## Previous follow-on slice: acceptance checklist evidence parity

The Angular assessment projection now carries the evidence reference from a
prior continuance answer through its strict response decoder and renders it
beside that prior answer, matching the legacy checklist. A PostgreSQL-backed
API-host browser journey seeds an earlier continuance response and verifies the
reference is rendered after the next evaluation opens. Angular unit tests and
the production build passed; the build retains its existing initial-bundle
budget warning. The built-in browser showed the expected generic unavailable
state for the unassigned Development identity, while the authorized synthetic
flow passed in Playwright.

The `AcceptanceChecklistPanel` source row is now PARTIAL with a hash-checked
action mapping. The route's narrower Senior/Reviewer denial remains an explicit
security boundary; this change does not broaden it. The complete role/scope,
validation, stale-content and command recovery matrix is still open. No EF
model-drift, whole-solution, production or owner acceptance is inferred.
Exact evidence and current inventory counts are in [`status.json`](status.json);
Blazor retirement remains `NOT_READY`.

## Previous follow-on slice: audit planning and fieldwork source review

Six source pages and two embedded panels now have hash-checked partial reviews
against their Angular, API and Application owners. Commit `efdc0738` adds an
Application projection for reviewer-only fieldwork access: reviewers receive
submitted procedures and review notes, while planner schedules, client-upload
candidates, physical files, and planning controls stay out of that response.
The Angular page displays only the review workflow for reviewer-only users;
the existing Application commands still enforce role and engagement scope.

The PostgreSQL-backed fieldwork browser journey covers a staged
client upload delivered by the simulated background worker, evidence linking,
MUS and SYSTEMATIC sampling, physical-file registration/link/movement, an ad hoc
step, and a reviewer-created note answered by the preparer and resolved by the
reviewer. It also asserts reviewer projection redaction and denial of sampling
for the Reviewer-only grant. The materiality browser journey separately passes
the self-approval boundary: the preparer sees no approval action and an
independent Manager approves the persisted calculation. Angular tests and the
production build passed with the existing initial-bundle budget warning. The built-in Development browser rendered the audit-plan alias
read-only; reviewer-only behavior was checked in the isolated Playwright
journey using synthetic identities.

The audit-plan alias and focused journeys passed; exact commands, source hashes
and attribution are in [`status.json`](status.json). At this review checkpoint,
`AcceptanceChecklistPanel` had not yet been reviewed; it is now covered by the
current follow-on slice above. The reviewed fieldwork and materiality panels
remain PARTIAL pending the full command/role/error/recovery matrices. No
full-suite, EF-drift, production, or
owner acceptance is inferred; Blazor retirement remains `NOT_READY`.

## Previous follow-on slice: accounting workspace and period maintenance

In the current code slice, the Angular evidence and mapping/journal/difference
queues regained the legacy 10/25/50 rows-per-page choices with a default of 25.
Unit tests cover page counts, selection and resetting to the first page.
API-host journeys cover draft period roll-forward with a separately reviewable
opening bridge, and restatement creation followed by approval from a different
reviewer while preserving both validated package hashes.

The focused PostgreSQL-backed Release cohort and complete Angular unit suite
passed, and the production bundle built with its existing small budget
warning. After reloading the authenticated Development Mappings
route in the built-in browser, the selected page size was 25. This was a
read-only check against an empty authorized queue. The other agent's
`ConsolidationOverviewQuery.cs` edit was not included in the code commit.

The accounting workspace and period-maintenance source family now has
hash-checked PARTIAL reviews and Angular/API/Application mappings. None is promoted to
full parity. The full solution regression and EF model-drift result were not
rerun for this slice; exact counts, commands and commit attribution are in
[`status.json`](status.json). Direct query/search/count/export/command
authorization and retry/idempotency checks remain open. Remaining
source/action reviews and supporting files, production rollback/canary, human
assistive-technology and wider-locale review, live Microsoft gates, and
separate owner acceptance keep Blazor retirement `NOT_READY`.

## Previous follow-on slice: accounting artifact source/action review

At code commit `6b1a7a1d`, the package-review selection now has an explicit
tab-only save/restore/discard flow. Restore filters stored package IDs against
the freshly loaded authorized queue; session invalidation clears selection and
preview. The PostgreSQL-backed browser journey saves a selected package,
reloads the route, restores it, previews it, and confirms preview made no
non-GET request. Angular unit coverage checks the tab draft boundary and stale
ID/session behavior. The built-in browser rendered the new controls with an
authorized empty Development queue; that observation was read-only.

At that earlier checkpoint, six pinned legacy source/action files in the
accounting artifact family had been checked against inventory hashes and
reviewed against Angular components, API endpoints, Application services and
focused journeys. All six remained `PARTIAL`; none was promoted to full
parity. The then-current aggregate was one parity-verified, 21 partial and 54
unanalyzed source/action rows, plus 28 supporting files. Later accounting
workspace/period-maintenance reviews supersede those counts. The removed
E2E-method crosswalk is 82/82. AS-PAR-002 still needs direct
query/search/count/export/command authorization and retry/idempotency checks.
Production rollback/canary, human assistive-technology and wider-locale
review, live Microsoft gates, and separate owner acceptance keep retirement
`NOT_READY`.

## Previous follow-on slice: Angular audit workflow parity

At test commit `cd162be1`, three API-host Angular journeys map the final five
removed `AuditAndReleaseJourneyTests` methods. The revocation journey opens
workpaper, finding and audit-plan pages in separate browser contexts, revokes
the current local role through `RoleAdministrationService`, then refreshes each
page and verifies the safe unavailable state contains none of the formerly
authorized values. The fieldwork journeys record a human aggregate difference
conclusion bound to the exact source snapshot, and publish/adopt the controlled
audit program before reviewed fieldwork and a frozen workpaper survive reload.

The focused PostgreSQL-backed Release E2E cohort passed 3/3 with no failures or
skips. The E2E project and API dependencies compiled in Release. This is a
test-only slice; no Application or EF model code changed, and the EF drift
check was not rerun. The complete-solution regression remains the earlier
1001/1001 checkpoint, not a run at this commit. The then-current 19 removed
`FinancialArtifactJourneyTests` methods and 60 source/action rows were open at
that historical checkpoint; later slices closed the removed-method crosswalk
and reviewed six accounting rows. The other agent's unstaged
`ConsolidationOverviewQuery.cs` edit was excluded. AS-PAR-002 remains partial
and Blazor retirement remains `NOT_READY`; production rollback/canary, human
assistive-technology acceptance, live Microsoft gates and separate owner
acceptance remain open. See [`status.json`](status.json) for exact counts and
command evidence.

## Previous follow-on slice: Angular completion, fieldwork and plan route parity

At test commit `814d6428`, `AngularClientScopeEngagementParityJourneyTests`
extends the API-host PostgreSQL parity checks for engagement audit routes.
Completion now proves that a same-document transition to an unassigned sibling
engagement clears the prior representation narrative and code, then restores
the authorized record without replacing the document. Fieldwork publishes and
adopts the controlled program, verifies its procedure set, clears that program
on the sibling route and restores it on return. The audit-plan route retains
same-document denial/recovery checks. Completion and audit plan also check six
viewport widths and keyboard-visible focus.

The focused cohort passed with no failures or skips. The Angular production
build succeeds with the existing 6.60 kB initial-bundle budget warning. This is
test-only; no Application or EF model code changed. Three removed
`AuditAndReleaseJourneyTests` methods now have assertion-level Angular maps.
The latest full-solution regression remains the earlier 1001/1001 checkpoint;
this slice did not rerun the full solution. The other agent's unstaged
`ConsolidationOverviewQuery.cs` edit was excluded from the test commit. See
[`status.json`](status.json) for current crosswalk counts and exact evidence.
AS-PAR-002 and Blazor retirement remain partial/`NOT_READY`; production
rollback/canary, human assistive-technology acceptance, live Microsoft gates
and separate owner acceptance remain open.

## Previous follow-on slice: Angular audit-detail route parity

At API-host test commit `fc87cc66`,
`AngularClientScopeAuditDetailJourneyTests.AuditDetailRoutesClearSiblingAndRevokedContentInTheSameDocument`
adds same-client sibling-engagement targets for the workpaper, finding and
population details. The Angular route journey retains sibling-client and
guessed-ID checks, then proves protected values clear on denial and reload on
authorized return. It now checks all six viewport widths and keyboard focus on
the workpaper, finding, population, archive and review routes. The population
fixture includes a reviewed item test and sample rationale; the workpaper has a
frozen submission and the finding has a recorded management response.

The focused PostgreSQL-backed Release E2E run passed 1/1 with no failures or
skips in 37 seconds. The E2E project and API dependencies compiled in Release.
This is test-only; no Application or EF model code changed. It maps three
removed `AuditAndReleaseJourneyTests` methods. The removed-method crosswalk now
covers 55/82 methods, including all 39 `ClientScopeJourneyTests` methods and
five audit/release methods. Twenty-seven methods remain: eight in
`AuditAndReleaseJourneyTests` and 19 in `FinancialArtifactJourneyTests`. The
latest complete PostgreSQL-backed Release solution result remains 1001/1001 at
`ead85032`, before this focused test. Sixty source/action rows and 28
supporting files remain unanalyzed. AS-PAR-002 stays partial and Blazor
retirement stays `NOT_READY`; production canary, human assistive-technology
review, live Microsoft gates and separate owner acceptance remain open. Exact
evidence is in [`status.json`](status.json) and the [test parity
register](../migration/auditsphere-migration-blazor-test-parity.md).

## Previous follow-on slice: Angular release safety parity

At API-host test commit `e82c3d2b`,
`AngularReleaseParityJourneyTests.ExpiredProtectionBlocksIssueAndReleaseDetailsClearOnScopeChangeAndRevocation`
maps the removed release-candidate route and expired-protection journeys.
Against an isolated PostgreSQL fixture, the Angular page shows the exact
candidate only to the authorized engagement Partner, displays the expired
protection attestation as expired (never verified), keeps issuance blocked,
and claims no external records-provider acceptance. The Application issue
attempt is refused and creates no Release row. A sibling-engagement Staff user
and a guessed candidate ID receive safe unavailable states without release
details; same-document navigation clears and restores data correctly. The
journey also checks six viewport widths, keyboard focus, and stale state after
grant revocation and reload.

The focused Release E2E run passed 1/1 with no failures or skips in 29 seconds.
The E2E project and API dependencies compiled in Release. This is test-only;
no Application or EF model code changed. The removed-method crosswalk now
covers 52/82 methods, including all 39 `ClientScopeJourneyTests` methods and
two release-safety methods. Thirty methods remain across
`AuditAndReleaseJourneyTests` (11) and `FinancialArtifactJourneyTests` (19).
The latest complete PostgreSQL-backed Release solution result remains
1001/1001 at `ead85032`, before this focused test. Sixty source/action rows and
28 supporting files remain unanalyzed. AS-PAR-002 stays partial and Blazor
retirement stays `NOT_READY`; production canary, human assistive-technology
review, live Microsoft gates and separate owner acceptance remain open. Exact
evidence is in [`status.json`](status.json) and the [test parity
register](../migration/auditsphere-migration-blazor-test-parity.md).

## Previous follow-on slice: Angular assessment, engagement and completion scope parity

At test commit `bfb72c71`, `AngularClientScopeEngagementParityJourneyTests`
adds API-host Playwright replacements for the five remaining
`ClientScopeJourneyTests` methods. Isolated PostgreSQL scenarios cover
canonical and `/ui` client-assessment deep links, Partner-only decision
visibility, unrelated-client denial, engagement detail and audit-plan route
changes, completion representation privacy, and current-grant revocation.
The stale audit-risk command is refused with `GenerationStale`, no row is
persisted, and the open browser session is invalidated.

The focused Release E2E cohort passed 4/4 with zero failures or skips in 1m23s.
This is test-only: no application or EF model changed. The removed-method
crosswalk at that slice covered 50/82 methods, including all 39
`ClientScopeJourneyTests` methods; 32 methods remained in
`AuditAndReleaseJourneyTests` and `FinancialArtifactJourneyTests`. The latest
complete PostgreSQL-backed Release solution result remained 1001/1001 at
`ead85032`. Sixty source/action rows and 28 supporting files remained
unanalyzed. AS-PAR-002 stayed partial and Blazor retirement stayed
`NOT_READY`. Exact evidence is in [`status.json`](status.json) and the [test
parity register](../migration/auditsphere-migration-blazor-test-parity.md).

## Previous follow-on slice: Angular accounting task-owner scope parity

At code commit `73a2d51b`, the Angular accounting workspace regains the missing
engagement task-owner summary through `AccountingTaskOwnerQuery`, a protected
same-origin API endpoint and a bounded Angular table. The projection includes
only active task counts and staff display names, requires a current accounting
grant covering each engagement and a matching stored client relationship, and
rechecks grants before returning. It omits task titles and owner IDs, so this
does not widen client-wide books or period access.

The PostgreSQL-backed API-host Playwright cohort passed 2/2 in 41 seconds. It
maps `AccountingWorkspaceHidesSiblingEngagementTaskSummary`, verifies assigned
owner visibility, sibling-owner exclusion, absence of task titles/IDs and
clearing after grant revocation; the adjacent accounting workspace scope
journey also passed. Angular CI passed 477/477 tests across 91 files. The
production build succeeded with the existing initial-bundle warning (506.60
kB, 6.60 kB over budget). The built-in browser loaded the Angular accounting
page against the refreshed local API and showed its authorized-scope empty
state for the Development identity; no command was submitted.

The removed-method crosswalk now covers 45/82 methods, including 34
`ClientScopeJourneyTests` methods; 37 methods across three suites remain open.
The latest complete PostgreSQL-backed Release solution run remains 1001/1001
at `ead85032`, before this code commit; current evidence is focused, not a new
whole-solution regression. No EF entity/model changed, and the prior model
drift check remains the latest. The concurrent unstaged
`ConsolidationOverviewQuery.cs` edit was excluded from the commit. AS-PAR-002
remains partial and Blazor retirement stays `NOT_READY`. Exact evidence is in
[`status.json`](status.json) and the [test parity
register](../migration/auditsphere-migration-blazor-test-parity.md).

## Previous follow-on slice: Angular archive and review route parity

At test commit `6e48c977`, `AngularClientScopeAuditDetailJourneyTests` adds the
legacy archive and review-point assertions that were still missing. For an
authorized archive, the API-host Angular journey verifies the manifest
summary and entry count, all six viewport widths, keyboard focus, exact and
guessed-ID denials, same-document clearing, and authorized return. For a
significant review point, it checks the open completion blocker, responsive
layout and focus, same-document content/action clearing, and restoration.

The focused PostgreSQL-backed Playwright case passed 1/1 in 37 seconds. This
is a test-only change: no application code, API contract, database model or EF
migration changed. The latest complete Release solution run remains
1001/1001 at `ead85032`, before this test commit; current-source evidence is
focused, not a new whole-solution regression. The crosswalk now covers 44/82
removed E2E methods, including 33 `ClientScopeJourneyTests` methods; 38
methods across three suites remain open. Sixty source/action rows and 28
supporting files remain unanalyzed. AS-PAR-002 remains partial and retirement
stays `NOT_READY`. Exact evidence is in [`status.json`](status.json) and the
[test parity register](../migration/auditsphere-migration-blazor-test-parity.md).

## Previous follow-on slice: Angular PBC authorization parity

`AngularPbcScopeParityJourneyTests` now has four API-host Playwright journeys
that map eight removed `ClientScopeJourneyTests` methods. The isolated
PostgreSQL-backed cases cover client visibility for assigned engagements and
recipients, hidden unsent drafts, exact-ID denial, staff role-specific inbox
access, and same-document clearing when navigating between authorized and
unauthorized requests. They also revoke staff and client grants while their
workspaces are open, then prove the next command clears protected content and
persists no communication or reply. The added staff inbox route case navigates
to a sibling engagement in the same Angular document, clears both request
markers, then restores the assigned request on return.

The focused Release cohort passed 4/4 with zero failures or skips in 1m11s.
This is a
test-only slice: no application, API contract, database model, or EF migration
changed. The latest complete Release solution run remains 1001/1001 at
`ead85032`, before this PBC test commit; the current source has focused, not
whole-solution, regression evidence. The removed-suite assertion crosswalk now
covers 42/82 methods, including 31 `ClientScopeJourneyTests` methods; 40
methods across three removed suites remain open. Sixty source/action rows and
28 supporting files remain unanalyzed. AS-PAR-002 is partial and Blazor
retirement remains `NOT_READY`. The concurrent agent's unstaged
`ConsolidationOverviewQuery.cs` change was excluded from the test commit.
Exact evidence is in [`status.json`](status.json) and the [test parity
register](../migration/auditsphere-migration-blazor-test-parity.md).

## Previous follow-on slice: Angular client scope and export parity

Code commit `beee6480` extends the assertion crosswalk for eight removed
`ClientScopeJourneyTests` methods. The additions cover an erroneous firm-wide
Staff grant on a Client identity, portfolio export after role revocation,
engagement-only time queues, exact assessment deep links, client-scoped
accounting evidence, accounting workspace/period scope, and client-versus-
engagement scope for period-maintenance queues. A 401 from an Angular download
now refreshes the session so protected UI state is cleared; the Angular API
unit test covers that behavior.

The focused PostgreSQL-backed API-host Playwright cohort passed 15/15 with no
failures or skips. It exercises the canonical and `/ui` assessment routes,
exact and guessed IDs, stale-content removal after revocation or route change,
client/engagement scope isolation, and prevention of a post-revocation CSV
download. Angular CI passed 477/477 tests across 91 files. The production UI
build completed with its existing initial-bundle warning: 506.60 kB against a
500 kB budget. The Release solution build passed with zero warnings/errors;
EF reported no pending model changes. The Release build included an unrelated
unstaged `ConsolidationOverviewQuery.cs` edit from the concurrent agent, which
was excluded from the code commit. The built-in browser rendered the local
Angular accounting route read-only; no business command was submitted.

The latest complete suite remains the earlier 1001/1001 frozen pass at
`ead85032` (API 191, Domain 660, E2E 150), before this slice's code commit.
The current code changes therefore have focused, not whole-solution,
regression evidence. The removed-suite crosswalk now covers 34/82 methods;
48 methods across three suites remain open. Sixty source/action rows and 28
supporting files are still unanalyzed. AS-PAR-002 remains partial, and the
Blazor retirement decision stays `NOT_READY`. Exact evidence is in
[`status.json`](status.json) and the [test parity register](../migration/auditsphere-migration-blazor-test-parity.md).

## Previous follow-on slice: firm administration and workspace revocation

`AngularFirmScopeRevocationParityJourneyTests` covers the firm-administration
and current-access boundaries from five removed `ClientScopeJourneyTests`
methods. A Client identity carrying an erroneous firm-wide Administrator grant
is sent to the client portal; both administration overview and user-access
API reads are denied. The administrator browser journey verifies that a
revoked invitation grant cannot copy a link and that the stale copy control is
removed after the access projection refreshes. Revoking the open
Administrator, RelationshipManager or FinanceReviewer grants clears private
content from the already-open Users & Access, Operations, Practice leads and
Firm ledger Angular workspaces after refresh.

The same PostgreSQL-backed browser test checks the operations mode and
attention count, six viewport widths, keyboard focus visibility, retained
document tokens, and absence of page errors. It exposed a stale invitation
projection: the UI previously kept offering a copy action after the linked
grant was revoked. The Application query now reports that invitation as
revoked and exposes no copyable ID; the Angular client refreshes its access
projection after a refused copy attempt. The focused browser journey passed,
the Angular unit suite passed, the Release solution build passed without
warnings or errors, and EF reported no pending model changes. The complete
solution regression is running on the shared worktree, which includes the
concurrent edit; its final result and scope will be recorded in `status.json`.

The aggregate removed-method and source-action counts are tracked in
`status.json`. Production rollback/canary, human assistive-technology and
wider-locale review, live Microsoft gates, and separate owner acceptance
remain open. Retirement remains `NOT_READY` and AS-PAR-002 remains partial.

## Previous follow-on slice: advanced group consolidation authorization

`AngularAdvancedConsolidationAuthorizationJourneyTests` maps two removed
`ClientScopeJourneyTests` methods. A Client identity carrying an erroneous
`AccountingPreparer` group grant is redirected to its portal and receives a
4xx response from the protected advanced-consolidation API, with no group name
or scope ID disclosed. A Staff identity with the exact group grant can read
the authorized scope. In the same Angular document, changing from that scope
to an unauthorized sibling clears the prior group details; returning restores
authorized data, the document token remains, and the browser-local schedule
draft survives reload.

The focused API-host PostgreSQL/Playwright cohort passed. It checks
320, 390, 760, 1024, 1440 and 1920 pixel widths, bounded table scrolling and
keyboard-visible focus. The journey found and fixed a root horizontal scroll
caused by screen-reader-only table header text escaping its scroll container.
The advanced form now fits narrow screens, and Material buttons receive an
explicit visible keyboard focus outline. Angular production build succeeded
with a warning that the initial bundle is slightly over its 500 kB budget. The
Release solution build passed with zero warnings/errors; EF reported no pending model changes.
The built-in browser rendered the live Development group page, showed its
safe `No group access` state, displayed a solid focus outline after keyboard
navigation, and recorded no console errors. The full solution suite was not
rerun; its latest complete result remains attributed in `status.json`. The
unrelated `ConsolidationOverviewQuery.cs` edit was excluded from this commit.

Aggregate removed-method and source-action counts are tracked in `status.json`.
Production rollback/canary, human assistive-technology and
wider-locale review, live Microsoft gates, and separate owner acceptance
remain open. Retirement remains `NOT_READY` and AS-PAR-002 remains partial.
Exact attribution is in `status.json` and the [test parity
register](../migration/auditsphere-migration-blazor-test-parity.md).

## Previous follow-on slice: firm-wide finance and commercial scope

At test commit `fb208af7`, `AngularFirmWideScopeJourneyTests` maps
`ClientScopeJourneyTests.FirmLedgerAndLeadsRequireFirmWideRoleGrants`. The
API-host Angular journey verifies that a firm-wide FinanceReviewer sees the
firm period and account while client-scoped FinanceManager and Partner users
see neither. It also verifies that a firm-wide RelationshipManager sees the
commercial lead, a client-scoped RelationshipManager does not, and a Client
identity with an erroneous local RelationshipManager grant stays in the client
portal without the lead data.

The focused PostgreSQL-backed Playwright case passed 1/1 in 41 seconds. The
Release solution build passed with zero warnings/errors, EF reported no pending
model changes, and the built-in browser rendered the firm ledger's safe
restricted state for the current Development identity. The full solution suite
was not rerun; the latest complete regression remains 985/985 at `eb94ae50`.
An unrelated unstaged edit to `ConsolidationOverviewQuery.cs` was excluded from
the test commit.

Nineteen of 82 removed E2E methods now have assertion maps; three suites and 63
methods remain open. Sixty source/action rows and 28 supporting files remain
unanalyzed. Production rollback/canary, human assistive-technology and
wider-locale review, live Microsoft gates, and separate owner acceptance
remain open. Retirement remains `NOT_READY` and AS-PAR-002 remains partial.
Exact attribution is in `status.json` and the [test parity
register](../migration/auditsphere-migration-blazor-test-parity.md).

## Previous follow-on slice: client profile sibling-route isolation

At test commit `fbca7aea`, the API-host Angular client-profile journey maps two
removed `ClientScopeJourneyTests` methods. In both `/ui` and canonical route
modes, client-A Staff and client-B Manager identities each read their own
profile, then change to the sibling client's ID in the same document. The
route clears private profile details; a random ID returns the same denial text.
Returning to the authorized client restores its profile while a document
token remains. The current journey also retains the profile's paging, reload,
mobile, unknown-ID and revocation checks.

The focused PostgreSQL-backed Playwright profile cohort passed 2/2 in 48
seconds. The Release solution build passed with zero warnings/errors, EF
reported no pending model changes, and the built-in browser rendered the
generic unavailable state for an unknown client ID. The full solution suite
was not rerun; the latest complete regression remains 985/985 at `eb94ae50`.
An unrelated unstaged edit to `ConsolidationOverviewQuery.cs` was excluded from
the test commit.

Eighteen of 82 removed E2E methods had assertion maps at that slice; three
suites and 64 methods remained open. Sixty source/action rows and 28 supporting files remained
unanalyzed. Production rollback/canary, human assistive-technology and
wider-locale review, live Microsoft gates, and separate owner acceptance
remain open. Retirement remains `NOT_READY` and AS-PAR-002 remains partial.
Exact attribution is in `status.json` and the [test parity
register](../migration/auditsphere-migration-blazor-test-parity.md).

## Previous follow-on slice: client scoped audit detail and revocation journeys

At test commit `cf609420`, `AngularClientScopeAuditDetailJourneyTests` covers
five removed `ClientScopeJourneyTests` methods for finding details, population
and linked receipts, workpaper/submission history, review-point disposition,
and review-point grant revocation. The API-host Angular journey seeds real
client A and sibling client B records, verifies authorized details, compares
sibling and guessed-ID states, changes record IDs in place, and checks that
returning to client A restores only authorized content. Receipt filenames
remain hidden even on the authorized population page, as required by the old
privacy assertion.

The journey records a review-point clear and reopen, then revokes the exact
staff grant while two pages are open. Post-revocation review disposition and
workpaper draft commands are refused; neither state is written, and both pages
clear protected content. The PostgreSQL-backed Playwright case passed 1/1 in
33 seconds. The Release solution build passed with zero warnings/errors and EF
reported no pending model changes. A read-only built-in-browser check of an
unknown population ID showed the generic unavailable state. An unrelated
unstaged change to `ConsolidationOverviewQuery.cs` was present during
verification and excluded from the test commit.

Sixteen of 82 removed E2E methods had assertion maps at that slice; three
suites and 66 methods remained open. This journey also exercises archive and
review-point same-document sibling/guessed-ID clearing, but does not close
those old route-specific methods because their viewport, keyboard-focus, and
archive-summary assertions are not all represented. Sixty source/action rows
and 28 supporting files remained unanalyzed. The latest complete solution
regression remains 985/985 at `eb94ae50`; the slice ran its focused case, not
the full solution suite. Production rollback/canary, human
assistive-technology and wider-locale review, live Microsoft gates, and
separate owner acceptance remained open. Retirement and AS-PAR-002 remained
`NOT_READY`/partial. Exact attribution is in `status.json` and the [test
parity register](../migration/auditsphere-migration-blazor-test-parity.md).

## Previous follow-on slice: Practice Billing and Ledger assertion crosswalk

At test commit `18994e21`, all four methods removed with
`PracticeBillingLedgerJourneyTests` have method-level replacement maps. The new
API-host Angular journey records and independently approves staff time, binds
the approved time source once to an invoice, preserves invoice balance and
credit/receipt state, verifies billing does not create firm journals, posts
three manual journals idempotently, then closes the period through the Angular
firm-ledger route. Separate journeys check invoice content clearing on role
revocation and firm-wide proposal access, latest-revision details, unknown-ID
stale-content clearing, grant revocation, and scoped-user denial. The client
conversion journey now also asserts a pending acceptance decision with no
engagement or client portal access.

The combined API-host PostgreSQL/Playwright cohort passed 8/8 with zero failures
or skips in 2m19s. The Release solution build passed with zero warnings/errors;
EF reported no pending model changes. A concurrent, unrelated, unstaged change
to `ConsolidationOverviewQuery.cs` was present during the local run and was not
included in the test commit. The built-in browser rendered
`/ui/app/finance`; the current Development identity has no firm finance grant,
so the page showed its restricted state. Authorized period-close behavior was
verified only with the isolated synthetic test identity.

Eleven of 82 removed E2E methods had assertion maps at that checkpoint; three suites and 71
methods remained open. Sixty source/action rows and 28 supporting files remained
unanalyzed. Production rollback/canary, human assistive-technology and wider
locale review, live Microsoft gates, and separate owner acceptance remain open.
Retirement stays `NOT_READY`. The last complete solution regression remains
985/985 at `eb94ae50`; this slice ran the combined focused cohort, not the full
solution suite. Exact attribution is in `status.json` and the [test parity
register](../migration/auditsphere-migration-blazor-test-parity.md).

## Previous follow-on slice: route, shell and responsive assertion crosswalk

At test/source commit `0a461bf` (the current-section shell behavior landed in
`468f4a03`), both `ResponsiveShellSweepTests` methods and the single
`RouteRenderSmokeTests` method have current API-host Angular assertion maps.
The route journey covers all 26 parameterless routes, page headings,
staff/client shell separation, the dynamic project-progress summary and all
published module/audit/shared bars, filtering, and denial for a non-administrator.
The responsive journey checks 24 parameterless staff routes
and 10 seeded detail routes at 320, 390, 760, 1024, 1440 and 1920 pixels. It
asserts no document overflow, at most one exact active link, and the owning
current navigation section. It also checks the project-progress summary and
filter, visible keyboard focus in the accounting queue, client-portal fit, and
a long client name. It found a 320px overflow in the breadcrumb row; wrapping
the breadcrumb navigation fixed it.

The lost-circuit assertion maps to
`AngularPendingOutcomeReconciliationTests.LostCommandResponse_SurfacesGlobally_DeepLinksAndClearsAfterAcknowledgment`.
Because Angular has no SignalR circuit, the replacement verifies the persisted
unknown-command outcome across full navigation, its owning-workspace deep link,
manual receipt verification, acknowledgment, and the absence of automatic
retry. This is an explicit transport change, not a claim that the Blazor
reconnect overlay exists in Angular.

The route/responsive E2E cohort passed 2/2 in 47 seconds; the recovery journey
passed 1/1 in 31 seconds. The Angular production build succeeded at 506.51 kB,
6.51 kB above its 500 kB warning budget. The Release solution build passed
with zero warnings/errors and EF found no pending model changes at `468f4a03`.
The built-in browser rendered the Development Period roll-forward page and
visually showed the Accounting section as current; its tab was restored to the
accounting overview. The last full solution regression remains 985/985 at
`eb94ae50`; no full suite was rerun for this slice.

Seven of the 82 removed E2E methods now have assertion maps. Four suites with
75 methods remain open. Sixty source/action rows and 28 supporting files also
remain unanalyzed. Production rollback/canary, human assistive-technology and
wider-locale review, live Microsoft gates, and separate owner acceptance remain
open; retirement stays `NOT_READY`. Exact attribution is in `status.json` and
the [test parity register](../migration/auditsphere-migration-blazor-test-parity.md).

## Previous follow-on slice: Microsoft 365 setup assertion crosswalk

The two methods removed with `M365SetupJourneyTests` now have method-level
replacement maps in the native Angular/API journeys. The approved initial
administrator flow binds the configured tenant and object identity, requires
explicit proof review, refuses mismatched identities, closes the setup once,
and leaves the bootstrap session without protected workspace access. The
existing administrator opens the saved setup workspace without re-entering an
installation proof. Reviewed setup metadata and operation receipts survive
reload; exact selected SharePoint resource fields and template approval persist,
while changing the resource invalidates selected-site verification and keeps
activation blocked.

The old editable tenant ID and fixed `1/7` to `2/7` counter are superseded by
the authenticated deployment-approved tenant and progress derived from saved
capability/setup state. Saved metadata remains explicitly separate from consent
and live verification. The focused PostgreSQL-backed API/Angular Release cohort
passed 9/9 at code commit `fe95bc88`, the Release solution build passed with
zero warnings/errors, and EF reported no pending model changes. The built-in
browser rendered the authenticated Development tenant-connection screen and
showed consent separately from stale capability states; no setup or verification
action was submitted. No full solution suite was rerun, so the last 985/985
result remains attributed to `eb94ae50`.

At that checkpoint, four of the 82 removed E2E methods had assertion maps: the
PBC upload, invoice-scope and two M365 setup methods. Six suites containing 78
methods remained open. Sixty source/action rows and 28 supporting files also remain
unanalyzed, and production, human accessibility/locale, live Microsoft and
owner-acceptance gates remain open. The retirement decision remains
`NOT_READY`. Exact results are in `status.json` and the [test parity
register](../migration/auditsphere-migration-blazor-test-parity.md).

## Previous follow-on slice: invoice scope and stale-route content isolation

At code commit `500eda46`, the removed `InvoiceScopeJourneyTests` method has a
passing API-host Angular replacement. The isolated PostgreSQL Playwright case
seeds two client-scoped FinanceManagers and real invoices. Each manager can
read their own invoice; same-document Angular route changes to the other
client's invoice show the generic unavailable state without either client's
invoice number, line item or amount, keep the document token, and reload the
authorized invoice when returning. A second context verifies the reciprocal
denial and no page errors. The focused E2E case passed 1/1. A read-only
built-in-browser visit to an unknown invoice ID also rendered the generic
unavailable state and returned to `/ui/app/accounting`.

The Release solution build passed with zero warnings/errors and EF reports no
pending model changes at this code commit. No full solution test run was made
for this slice; the last 985/985 full regression remains attributed to the
earlier frozen commit in `status.json`. Two of 82 removed E2E methods now have
assertion maps; seven suites with 80 methods remain open. Sixty source/action
rows and 28 supporting files also remain open, along with production,
assistive-technology, locale, live Microsoft and owner-acceptance gates. The
retirement decision stays `NOT_READY`. See the [test parity register](../migration/auditsphere-migration-blazor-test-parity.md)
and [stop-gate report](../migration/auditsphere-migration-blazor-retirement-final-report.md).

## Previous slice: PBC upload recovery and removed-test crosswalk

The PBC source review compares the restored `PbcRequests.razor` staff inbox and
`ClientPbcRequest.razor` client route against API-host Angular journeys. The
client journey verifies accessible drag/drop, browser-computed SHA-256, staged
receipts, reply, delegation and revocation. The multi-chunk recovery journey
holds the resumed second chunk to verify in-flight progress and also covers lost
acknowledgement, capability rotation and exact-byte reconstruction. The staff
journey checks request creation, queued follow-up communication, worker
completion, refreshed `COMPLETED` / `RECEIVED` UI, actual browser download bytes,
and safe API response headers. A new uncertain-outcome journey has the simulated
provider accept exact bytes and lose its response; the inbox renders `staged` /
`result uncertain`, then reconciliation verifies one provider object and the UI
recovers to `RECEIVED` / `COMPLETED`. It checks 320, 390, 760, 1024, 1440 and
1920 pixel widths and keeps the recipient control in view. The assigned client
is also denied download access to the same received upload.

The focused PBC E2E cohort passed 4/4, the scoped API download test passed 1/1,
and the Release solution build passed with zero warnings/errors at the code
commit recorded in `status.json`. The single removed
`PbcUploadJourneyTests.ClientUpload_ReconcilesUncertainProviderResult_AndStaffDownloadsExactBytes`
method now has a passing assertion-by-assertion replacement map. Seven other
removed E2E suites and 80 methods remain open. Both PBC source rows stay
`PARTIAL`: live selected-site worker delivery through Angular and the wider
authorization/state/error/stale matrix remain open. Details and exact source
attribution are in `status.json`, the [test parity register](../migration/auditsphere-migration-blazor-test-parity.md),
and the [PBC source review](../migration/auditsphere-migration-blazor-pbc-source-review.md).
The current inventory is one source/action row parity verified, 15 partially
reviewed (13 administration and two PBC), and 60 unanalyzed; 28 supporting files
and the broader operational and owner gates remain open. Retirement stays
`NOT_READY`.

## Previous follow-on slice: approved initial-administrator sign-in

At code commit `e61d147`, Development/Test authentication now lets only the exact
deployment-approved synthetic tenant/object identity reach proof-backed
installation setup before a local user exists. That cookie has no session epoch,
so protected workspace APIs deny it. Bootstrap creates the local `AppUser` and
firm-wide Administrator grant through the existing reviewed transaction; a new
sign-in receives the normal session epoch. Production remains on Microsoft OIDC.

The rebuilt API-only browser journey passed 3/3 cases: approved bootstrap,
wrong-tenant denial and wrong-object denial. It also verifies pre-bootstrap
workspace denial, proof consumption and no proof persistence. Related API
installation/cookie-security tests passed 8/8. A full serial E2E compatibility
run passed 135/135 in 34m07s; it included the bootstrap happy path, while the
two identity-mismatch cases were added afterward and are covered by the focused
3/3 run. Release solution build passed with zero warnings/errors, Angular CI
passed 476/476 across 91 files, and EF reported no model drift. The Angular
build succeeded with the existing 505.53 kB initial bundle warning (5.53 kB
above budget). Exact attribution is in `status.json` under
`angularBlazorApprovedBootstrapSlice`.

The built-in browser rendered the authenticated Microsoft tenant-connection
route read-only and showed consent `VERIFIED` separately from stale directory
and selected-resource capabilities. No verification action was submitted.
At the bootstrap evidence checkpoint, Blazor removal remained `NOT_READY`: 62
source/action rows, 28 support files, the 82-method removed-suite crosswalk,
production/human/external checks, and separate owner acceptance remained open.
The later PBC review above supersedes its source-row counts.

The canonical removal gate remains `NOT_READY`. Commit `59387b54` removed
`AuditSphereOps.Web` and its Razor sources before the canonical source-action
register was complete. That repository state is not accepted as proof of
behavioral parity or a completed retirement. The new duplicate retirement
report was withdrawn; the canonical migration documents indexed from
`docs/auditsphere-docs-index.md` remain authoritative.

The Web rollback/reference project and Razor source have been restored to the
solution from the parent of the premature physical-removal commit. The API
remains the Angular route owner; API.Tests and E2E.Tests do not reference Web,
and the E2E fixture starts only the API. The recorded `eb94ae5` snapshot passed
the Angular production build and unit suite, Release solution build, complete
PostgreSQL-backed solution suite, and EF model check; exact evidence is in
`docs/execution/status.json`. A successful build and local browser smoke are not
an operational rollback exercise. Source-action parity, a complete replacement
crosswalk for nine legacy E2E suites, production canary/rollback, human
assistive-technology, locale, and live Microsoft acceptance remain separate
gates. See the canonical removal gate and current `verification` entries in
`docs/execution/status.json` for observed evidence.

Starting from master commit `8ea3ef01`, the test host was converted to API-only and
the API.Tests/E2E.Tests project graph no longer references Web. That commit also deleted nine legacy E2E suites
(`AuditAndReleaseJourneyTests`, `ClientScopeJourneyTests`,
`FinancialArtifactJourneyTests`, `InvoiceScopeJourneyTests`,
`M365SetupJourneyTests`, `PbcUploadJourneyTests`,
`PracticeBillingLedgerJourneyTests`, `ResponsiveShellSweepTests`, and
`RouteRenderSmokeTests`). This removes legacy host coupling but does not replace
their assertions. An assertion-by-assertion replacement map and full current-head
regression are still required; do not treat the new host topology as migration
parity. The current frozen Release regression passed, but it does not close the
assertion crosswalk. This working slice also replaces five stale M365 browser
journeys with Angular/API-host journeys; the M365 test catalog now points to the
replacement classes. The removed-suite assertion crosswalk remains open.

## Historical checkpoint: Blazor test-host decoupling for migrated Angular E2E journeys

This evidence belongs to commit `c562da2f109ce26ab2f061c00976823ff06aea61`; it predates commit `59387b54`, which removed the Web source while the canonical readiness gate remained open. It is not evidence of current-head retirement acceptance.

`OwnedBlazorHost.StartAsync` now has an explicit API-only mode. Its default still
starts the Test-only Web host for legacy journeys; migrated Angular E2E callers
that use the API host opt out. The resource-planning journey also asserts that
API-only mode created no Web-host log. This reduces unnecessary legacy runtime
use while preserving tests that specifically exercise Blazor.

At code commit `c562da2f109ce26ab2f061c00976823ff06aea61`, the E2E Release
project build passed with zero warnings/errors, the focused resource-planning
journey passed 4/4, and the complete E2E project passed 219/219 with zero failed
or skipped tests (45m46s). The run includes retained Test-only legacy journeys.
The E2E project still references Web, and this was not a full-solution run; no
Web source or project was deleted. Exact evidence is in `status.json`.

## Source-action parity: Partner assessment decision

The historical `AssessmentDecision.razor` source at the discovery snapshot was
compared with the Angular assessment route and its cookie/CSRF API. Review found
and fixed a disclosure that let a client-scoped Senior view assessment data
through the legacy Partner-only `/decision` URL. A distinct Partner-only API
resolver and narrower assessment-read role set now pass the PostgreSQL-backed
role test (1/1) and API-only Playwright journeys (2/2 across canonical and `/ui`
routes). The Senior receives a generic denial with no profile; the Partner
completes the reviewed decision and recovers one accepted receipt after a lost
response. `AssessmentDecision.razor` is `PARITY_VERIFIED` for this source only.

`AssessmentDetail.razor`, `AcceptanceChecklistPanel.razor`, the remaining 60
unanalyzed source/action rows, all 28 supporting files, and complete removed-test
crosswalk remain open. Fifteen artifacts are now separately `PARTIAL_REVIEWED`:
13 administration and two PBC. The overall physical-removal decision remains `NOT_READY`;
exact evidence and current rerun state are in `status.json`.

## API test host no longer depends on the Blazor project

API.Tests now references `AuditSphereOps.Api` directly and all API HTTP test
factories use `StandaloneApiApplicationFactory`. The former Web-bound API test
factory was removed; the API test lock file no longer brings in MudBlazor or
Blazor component packages. The sign-in landing assertions now pin the API's
Angular route destinations.

At code commit `d4439483`, the API.Tests Release build passed with zero
warnings/errors and the complete PostgreSQL-backed API test project passed
189/189 with zero failed or skipped tests. This does not remove the E2E.Tests
Web reference or its retained legacy smoke coverage, and it is not a
whole-solution regression. Exact evidence is in `status.json`.

## Preserve the route-ownership check without compiling Web

`AngularLegacyRouteInventoryContractTests` reads the committed source-discovery
manifest and verifies that every non-server-owned discovered legacy workspace
route has a declared API/Angular owner. This keeps the route-ownership
assertion executable without a Web project reference or Razor source tree. It
does not establish behavior parity, and the existing `AngularRoutingContractTests`
still reads Web source in the committed source snapshot.

At code commit `b2769fb8d6d2881b6feca2cbe96cbdb66f3b126e`, the API.Tests Release
project build passed with zero warnings/errors and the focused inventory route
contract passed 1/1. A concurrent lane had modified the older route test and
staged Web deletions in the shared worktree; those changes were excluded from
this commit, and the focused filter exercised only the new manifest contract.
The full API suite result remains 189/189 at `d4439483`; a full solution
regression was not run at this source commit. Exact evidence is in `status.json`.

## AS-PAR-002: client-scoped audit fieldwork command isolation

PostgreSQL-backed regressions seed sibling clients A and B and use real
client-B record identifiers. A Partner scoped only to A is denied audit-program
adoption, procedure applicability changes, result submission and result review;
the target procedure, result, workpaper and review state remains unchanged. The
confirmation lifecycle test denies A-scoped create, approval, dispatch, response,
response review, alternative work, alternative review and closure against B's
engagement and confirmation records; B's confirmation, response,
alternative-work and closure state remains unchanged. The difference test
denies A-scoped creation, aggregate reads, evaluation, correction-state changes
and journal linking against B's engagement and difference; the persisted
difference remains unchanged. The area-assessment case denies A-scoped
assessment creation, completion evaluation and review against B's engagement,
leaving B's assessment unchanged. The opening-balance case denies A-scoped record,
review and planning-summary requests for B and preserves B's verification. The
finding case also denies A-scoped creation, management-response recording and
management-letter designation for B's engagement, preserving B's finding state.
The planning case denies A-scoped materiality, risk, population and workpaper
creation, along with materiality approval and workpaper submission against B;
the existing materiality and workpaper remain unchanged.
A combined focused PostgreSQL-backed Release Domain regression run passed on the
current master commit. It also covers sibling schedule, selection, item-test and
sampling-run identifiers; those records remain unchanged after denial. Exact
evidence is in `status.json`. The full solution regression was not run for this
slice. A Domain suite attempt on the previous checkpoint stopped after approximately 13 minutes
without a runner summary, so no full-suite result is claimed. EF reports no pending
model changes. Exact evidence is in `status.json`. No shared Development
database, tenant, or production state was changed. AS-PAR-002 remains partial for other command
families and export/count paths, plus independent review.

## Parity hardening: workpaper navigation safety, management letter designation, and attribute strata sampling

This slice resolves key parity requirements across audit fieldwork, deliverable reporting, and sampling:
- **Workpaper Navigation Safety (GAP-01/UX10):** [WorkpaperEditor](../../src/AuditSphereOps.Ui/src/app/features/audit/workpaper.ts) guards in-flight and unacknowledged draft edits on navigation using `confirmNavigation()` with the shared [UnsavedChangesDialog](../../src/AuditSphereOps.Ui/src/app/core/unsaved-changes.ts) (Save, Discard, Stay), prevents browser window close via `@HostListener('window:beforeunload')`, and registers `canDeactivate: [unsavedChangesGuard]` on `/app/audit/workpapers/:id`.
- **Management Letter Finding Designation (GAP-02/R04):** [Finding](../../src/AuditSphereOps.Domain/Audit/Audit.cs) records maintain persisted designation decisions (`letter_designated_at`, `letter_designated_by_user_id`, `letter_recommendation`). [AuditDeliverableService](../../src/AuditSphereOps.Application/Completion/AuditDeliverableService.cs) filters exclusively for designated matters with complete recommendations when generating client-facing management letters; internal-only findings are excluded from client deliverables.
- **Reviewer-Defined Attribute Strata Sampling (GAP-03/T20/T22):** [AuditSamplingEngine](../../src/AuditSphereOps.Domain/Audit/AuditSamplingEngine.cs) implements attribute strata sampling supporting reviewer-selected fields (account, currency, direction, month), guaranteed stratum coverage, and persisted run provenance.
- **Commercial Quotation Recovery & Shell Pending Outcomes:** [QuotationWorkspace](../../src/AuditSphereOps.Ui/src/app/features/commercial/quotation.ts) persists save intent with unique request IDs for recovery without duplicated fee actions, and [PendingOutcomes](../../src/AuditSphereOps.Ui/src/app/core/pending-outcomes.ts) banner surfaces unresolved commands.

All 476 Angular unit tests, 647 Domain tests, and 186 API tests passed. EF Core migrations are in sync with 0 model changes pending.

## AS-PAR-002: portfolio isolation, billing commands, and stale resource review

The same-firm sibling-client differential now checks `/api/ui/portfolio/workspace`
and CSV export before and after client B receives marker data. The complete
workspace response and export stay identical for both CLIENT- and
ENGAGEMENT-scoped staff. A PostgreSQL-backed Application test also submits
client B's real account, invoice, and receipt identifiers to BillingService
reads and mutations as a client-A-scoped finance user; each is denied and the
invoice, account, receipt, allocation, and credit-note state remains unchanged.

Resource planning now has a stale-review browser journey in both `/app` and
`/ui` modes. A concurrent allocation update makes the reviewed command return
409; the UI identifies the stale review, removes the grid while the request is
unresolved, reconciles an absent receipt, and reloads the current 2-hour value
only after explicit close. The command is dispatched once.

At source commit `cdf18c014919fbfa2a89d125b706d9055036f627`, the API isolation
cases passed 2/2, the billing Application test passed 1/1, the focused Angular
resource suite passed 14/14, and the stale-review Playwright journeys passed
2/2. The Angular production build passed with a 505.46 kB initial bundle,
5.46 kB above its 500 kB warning budget. EF reported no pending model changes.
The full solution regression was not run. Built-in-browser navigation to
localhost was blocked by its URL policy; the isolated Playwright journeys used
owned PostgreSQL databases. AS-PAR-002 remains partial for other command
families and export/count paths; no shared Development migration or production
effect was performed.

## Resource grid separates approved actual hours and stays readable on mobile

Resource week cells now show approved actual hours separately from planned hours
and capacity, and include the actual amount in their accessible name. Minimum
column widths prevent the week headers from collapsing; narrow viewports scroll
inside the grid without widening the whole page.

The focused Angular resource suite passed 13/13. The PostgreSQL-backed Playwright
resource journey passed 2/2 on canonical and `/ui` routes, including a seeded
approved-time entry and 390px no-overflow/contained-scroll checks. The built-in
browser showed a 343px grid viewport, 896px scrollable grid and 112px week column
at 390px, with no console warnings or errors. The production Angular build
succeeded in a temporary output directory with a 5.46kB initial-bundle warning
over its warning budget. The shared-tree E2E attempt stopped before the page due
to concurrent pending EF model changes; the same journey passed from a clean
snapshot. No shared Development data was changed. The full suite and wider
staffing/budget acceptance remain open.

## Accounting preparation navigation reflects exact role and scope

The analytical preparation page showed the generic scope-denial message for a user
without an eligible preparation grant. The engagement page also advertised the
preparation workflows to staff who could not open them. The shared Application
authorization gate now exposes the exact existing role and client/engagement scope
capability to the engagement query, and the Angular navigation only shows those
links when that capability is present. The analytical page gives safe guidance to
ask a firm administrator to review the local role and scope. Protected direct
routes continue to enforce the existing server-side checks; no role or scope
permission was widened.

The focused Application, API, Angular, Playwright and built-in browser checks
passed on isolated synthetic data. The full solution regression was not run on
this source commit; its latest complete result remains tied to the earlier commit
recorded in `status.json`. No shared Development database data was changed. Exact
verification evidence is recorded in `status.json` under the local evidence entry
for this slice.

## Native reconciliation and specialist schedule preparation

Native reconciliation creation (`/app/engagements/:id/reconciliation/new`) and
specialist schedule preparation (`/app/engagements/:id/specialists/new`) are now
implemented with typed Signal Forms, exact server previews, explicit reviewed assent,
retained immutable actor-owned request receipts, tab draft persistence and
lost-response receipt recovery. Reconciliation creation binds exact client source
datasets, accounts, period context, reasons and evidence references, rejecting
cross-scope or unreviewed requests. Specialist schedule preparation composes
existing IAS 21 / IAS 16 asset and liability schedules with exact six-decimal inputs,
preview calculations, immutable snapshots, and actor-owned receipt recovery.
Unresolved command responses fence further writes and offer receipt verification on reload.

PostgreSQL-backed API tests (`AccountingPreparationCreationApiTests`) passed 2/2,
Playwright E2E journey (`AngularAccountingPreparationCreationJourneyTests`) passed 1/1,
all 460/460 Angular unit tests passed, and the Release solution build and production
UI build passed with zero warnings or errors. EF reported no pending model changes.
The additive migration `20261004121805_NativeReconciliationAndSpecialistPreparation`
was exercised in isolated synthetic test databases; shared Development and production
databases were not migrated. Broader reconciliation revision forms and overall migration
acceptance remain open.

The browser check exposed that the engagement menu treated `reconciliation/new`,
`specialists/new` and `analysis/new` as single router segments. Commit `935bd79`
now splits each configured path into real route segments. The focused engagement
route contract passed 3/3, an isolated production Angular build passed, and the
built-in browser opened all three forms at their intended URLs. The shared
Development client had no configured accounting profile, reporting period or
accepted source, so those forms displayed the safe unavailable state; no shared
data was changed.

The complete Angular unit suite passed 461/461 on commit `935bd79`, using a
clean temporary UI snapshot so the other agent's in-progress application shell
and quotation changes were not included. The full E2E rerun against isolated
assets was stopped after 44 minutes without a test summary; it remains
unverified, and no whole-suite pass is claimed.

The API and Domain projects also passed 186/186 and 647/647, respectively, on
feature commit `815d79f`. The focused preparation Playwright journey passed 1/1
using a clean isolated UI build. The first full E2E attempt could not start its
owned hosts because a parallel Angular test run had cleared shared `dist`. A
second attempt used the isolated UI bundle but was stopped after about 44
minutes without a runner summary; the test host had used up to about 1.5 GB and
sustained CPU. No whole-solution pass is claimed. The additive migration was
tested only against disposable PostgreSQL databases; Development and production
were not migrated.

## Angular proposal-create retry fingerprint and full regression

At pushed code commit `f92ae6887f56e20ed857a638a507099829132826`, keyed proposal
creation stores a canonical SHA-256 fingerprint of the reviewed request, bound
to the firm, actor, request identity, opportunity and expected revision. Retry
reconciliation compares that immutable fingerprint rather than mutable proposal
fields, so later quotation edits cannot make changed request terms look like an
exact retry. PostgreSQL guards reject fingerprint mutation or deletion, and the
rollback refuses to remove the migration while fingerprints remain. The
Angular page continues to persist the reviewed intent before dispatch and
requires fresh review before retrying after an uncertain response.

The serial Release solution suite passed API 182/182, Domain 647/647 and E2E
206/206: **1,035 passed, zero failed and zero skipped**. The Angular production
build, 452/452 Angular unit tests, Release solution build (zero warnings or
errors), focused PostgreSQL fingerprint test, focused lost-response browser
journey and EF model-drift check also passed. Exact evidence is in
`status.json` under `angularCommercialProposalCreateRecoveryFullRegression`.
The additive migration was exercised in isolated test databases only; the
shared Development and production databases were not migrated.

Overall Angular migration acceptance remains PARTIAL. Remaining local
creation/planning/source-action parity, production-like canary, real
screen-reader and wider-locale acceptance, and live Microsoft gates remain
open. No production deployment or live Microsoft mutation was performed.

## Angular commercial create-recovery full regression

At source commit `9820d128e54925a4a2bff99a5b44c5fc7d4edc82`, the serial Release
solution test run passed API 182/182, Domain 646/646 and E2E 205/205: **1,033
passed, zero failed and zero skipped**. It includes the lead- and
opportunity-create lost-response recovery journeys. EF reported no pending
model changes. The Angular production build, 452 Angular unit tests and Release
solution build had passed on the same code state before this documentation-only
checkpoint. The commit was the verified `master` remote head when the run began.
Exact metrics are recorded in `status.json` under
`angularCommercialCreateRecoveryFullRegression`.

This closes the full-suite verification gap for those two create-recovery
slices. The Angular migration remains PARTIAL: other creation/planning/source
actions, broader screen-reader and locale acceptance, a production canary, and
live Microsoft gates remain open. No production deployment or live Microsoft
mutation was performed.

## Angular commercial opportunity creation recovery

At code commit `cdc8c8f`, opportunity creation saves its reviewed terms and
request identity to the current user’s browser draft before dispatch. On reload,
the page queries the lead’s persisted opportunities. If the exact request ID is
present, it clears the pending intent and reports the persisted result without
posting again. If it is absent, the unchanged intent remains available for an
explicit retry after fresh review, using the same request ID. The API already
binds that request ID to the exact opportunity fields and current actor. No
database migration was needed.

The Playwright journey covered both outcomes: a committed write with a dropped
response was reconciled from the list, and an uncommitted write was retried with
the same ID after the user re-reviewed its terms. Two PostgreSQL Domain
idempotency cases, all 452 Angular unit tests, Angular production build, Release
solution build and EF model check passed. Exact evidence is in `status.json`
under `angularCommercialOpportunityCreateRecovery`. The complete solution suite
was not run on this source commit; the prior 1,029/1,029 result remains tied to
`32ad074`. Recovery for other commercial commands and overall migration
acceptance remain open.

## Angular commercial lead creation recovery

At code commit `0e1f874`, Angular lead creation saves the complete request and a
client-generated request ID before dispatch. If the response is lost, the form
locks its fields and offers an explicit recovery action after reload. The API
reuses the request ID as the lead identity and returns the existing lead only
when firm and submitted fields match; changed or cross-firm reuse fails with a
generic idempotency conflict. Legacy callers without a request ID retain their
existing behavior. No database migration was needed.

The lost-response Playwright journey committed a synthetic lead, dropped its
first response, reloaded and retried the same request ID; it observed exactly
one lead row. The PostgreSQL idempotency test, API contract test, all 452
Angular unit tests, production Angular build, Release solution build and EF
model check passed. Exact evidence is recorded in `status.json` under
`angularCommercialLeadCreateRecovery`. The complete solution suite was not run
on this source commit; the previous 1,029/1,029 result remains attributed to
`32ad074`. Broader commercial command recovery, overall migration acceptance,
production canary, real assistive-technology checks and live Microsoft gates
remain open.

## Historical checkpoint: owner-approved Angular route cutover and Blazor runtime retirement

At this 2026-10-04 checkpoint, the owner had approved completing the local cutover. The standalone API
is the only supported runtime and serves Angular on canonical `/app`, `/portal`
and `/setup` routes. `AuditSphereOps.Web` remains as a Test-only regression
fixture; `ApiHost` rejects `legacyPresentation=true` in every other environment.
Operator setup guidance now uses the API, and the accounting browser journey
script builds Angular, applies migrations through API and launches API.

At code commit `32ad074`, the Angular production build and Release solution build
passed with zero warnings/errors. Retirement policy tests passed 4/4, production
security configuration tests passed 5/5, and the Test-host route-render smoke
passed 1/1. The complete PostgreSQL-backed solution suite then passed in one run:
API 182/182, Domain 644/644 and E2E 203/203 (**1,029 passed, zero failed, zero
skipped**). EF reported no pending model changes. The built-in browser rendered
the authenticated Accounting workspace at `http://localhost:5099/app/accounting`
with canonical `/app` links and its authorized client table. No business
mutation was submitted. Two earlier full-suite attempts exposed security tests
that still booted the retired Web host in Production; those tests now use the
standalone API factory, and the final complete run passed.

The production Angular build was then published with the API. Its 105 browser
files contain no Blazor/MudBlazor runtime names or references, and the API
dependency manifest contains no Web or MudBlazor dependency. The focused route
contract suite passed 3/3: every Angular route has an API SPA owner, every legacy
Razor route has an explicit native owner except server-owned root/access-denied
routes, and authentication routes stay outside the SPA.

This historical checkpoint reported US-047 local runtime retirement complete.
That disposition was later superseded when the Web rollback/reference source
was restored at `eb94ae5`. The source action audit and assertion-level
replacement crosswalk for the 82 test methods in nine deleted E2E suites remain
open; the current removal gate is `NOT_READY`. Production
canary, real assistive-technology and wider-locale acceptance, and live Microsoft
gates also remain outside local verification.

## Current Release regression and resource-planning browser journey

At `152790fb1709d7c886b49be539260d6a84201afd`, the full Release solution test
invocation passed API 178/178 and Domain 644/644. E2E reported 202 passed and
one failure: the Angular resource-planning journey on the `/ui` preview route
could not find its review confirmation after profile submission. The journey
now submits an actual profile change and awaits both HTTP 200 from the preview
endpoint and the named review panel before checking that assent is disabled.
The focused journey passed both `/ui` and `/app` variants (2/2), and the full
E2E project passed 203/203 at `7fe9603721ee9352f6fd40bad1f24c5d5a1ecc28`.
The Release solution build after this test change passed with zero warnings and
errors. API and Domain production code was unchanged by the E2E-only correction;
however, there is not yet a single clean all-project test invocation at the
latest commit. The latest clean all-project run remains 1022/1022 at `404b4b8`.
Full migration acceptance remains PARTIAL.

## Angular invoice receipts, allocations and credit notes

The Angular invoice workspace shows billing-account receipts and invoice credit
notes in on-demand pages of 100 rows, records reviewed receipts, allocates
receipts to posted invoices, and issues reviewed credit notes for FinanceManagers.
Separate timestamp-and-ID keyset cursors preserve rows sharing the same timestamp.
The API delegates to the existing `BillingService`; the Application query scopes
the invoice, billing account, receipt history and credit history. Exact decimal
strings, complete invoice balance totals, explicit review, CSRF and current
role/scope checks remain in force. After a lost response, the form stays disabled
until persisted state is refreshed and the unresolved draft is cleared. Evidence
for the Release build, Angular suite, PostgreSQL API journey, Playwright browser
journeys and EF model check is in `status.json` under
`angularBillingReceiptCreditWorkspace`.

The built-in Development browser was reloaded on the authenticated Angular
workspace. Its synthetic search returned no invoice to inspect, so no Development
billing rows were created; the invoice and paging flow was exercised in owned
PostgreSQL Playwright journeys. Wider US-019 parity, production canary,
assistive-technology acceptance and live Microsoft gates remain open.

## Current-source Angular regression and built-in browser check

At source `404b4b8`, the Angular production build and Release solution build
passed with zero warnings and errors. The full PostgreSQL-backed solution run
passed API 176/176, Domain 644/644 and E2E 202/202: 1022 passed, zero failed
and zero skipped. EF reports no pending model changes. Exact evidence is in
`status.json` under `angularCurrentSourceFullRegression`.

The built-in browser first showed the Mappings route's safe retry state while
the Development database had pending migrations. A private custom-format
database backup was verified before the local migration update. The database
is now at migration `20261003153640_NativeTenantSetupMetadataReceipts`: all 142
repository migrations applied, none pending. After restarting the Release API,
both liveness and readiness returned 200. The browser reload showed the
authenticated Angular Mappings page and its authorized empty state (0 mapping
versions, 0 chart-bound); no business mutation was submitted. The backup stays
outside the repository. Exact observations are in `status.json` under
`angularCurrentSourceFullRegression`.

This verifies the earlier local Development route and empty-state journey.
Production canary, live Microsoft checks, screen-reader and wider-locale
acceptance remain open; overall migration acceptance is PARTIAL.

The owner-approved route-ownership change is committed as `f4afcc5` and
`b293ca1`: canonical `/app` routing is now the API configuration default, while
an explicit `false` retains `/ui` preview routing. The Angular production build,
Release solution build, standalone API publish and EF model check passed;
`CanonicalAngularHostTests` passed 4/4. The publish contains the canonical
setting and fingerprinted Angular assets. The built-in browser verified the
authenticated `/app/accounting/mappings` route and the `/ui` preview alias, with
navigation following each prefix. No business mutation or production deployment
was performed. The Web project is now Test-only and refuses non-Test startup.
Evidence is recorded under `angularCanonicalRouteDefaultFlip` and
`angularDevelopmentCanonicalRouteOwnership` in `status.json`.

The shipped API now defaults to canonical Angular ownership. Production-like
canary, deployment retention of the previous fingerprinted build, live
Microsoft acceptance, assistive-technology acceptance and Blazor retirement
remain open, so overall migration acceptance remains PARTIAL. Local runtime
retirement was recorded at the historical checkpoint above, then superseded by
the rollback-source restoration; current removal readiness remains `NOT_READY`.

## Reviewed existing service-period engagement inspection

The authorized client-scoped preview now returns the matching existing
engagement ID when the service route and period already exist. Angular shows a
separate existing-engagement state with an inspection link and no duplicate
creation confirmation. The Application write path repeats the duplicate check
inside the client safety transaction and rejects the write.

Angular component coverage, PostgreSQL-backed Domain and API tests, both
Playwright route modes, the Angular production build, Release solution build and
EF model check passed. Exact counts and tested source commit are recorded in
`status.json` under `angularExistingEngagementInspection`. The wider solution
suite was not run for this slice. US-015 and migration acceptance remain
PARTIAL; no production cutover or Blazor retirement is claimed.

## Current-head regression attempt (incomplete)

On the current code source (`b012022`), the Angular production build and Release
solution build passed with zero warnings or errors. API and Domain tests passed.
The serial E2E stage continued for
more than 37 minutes without a terminal summary and was stopped; no E2E count
or aggregate full-suite result is claimed. EF reported no pending model changes.
The earlier 1021/1021 clean frozen pass remains tied to its recorded checkpoint
and is not attributed to this later accounting source. Full current-source
regression and overall migration acceptance remain open. Exact evidence is in
`status.json` under `angularCurrentHeadRegressionAttempt`.

## Angular accounting alias and dimension review safeguards

Accounting chart, account, alias and dimension forms now clear review assent
when a reviewed value changes. Alias and dimension reads show safe loading,
failure and retry states; their mutation controls stay disabled until the
current client/revision read succeeds. Responses are bounded and validated,
with an explicit truncation notice where the read is larger than the display
limit. A client-scoped Playwright journey verified safe 503 recovery, reviewed
creation, persistence and display after reload. Angular tests, the production
UI build, Release solution build, EF drift check and staged secret scan passed.
The built-in browser loaded the authenticated Angular route and verified chart
and dimension assent invalidation on a synthetic client without submitting a
write; no console errors appeared.
The current-source full solution suite has not been rerun; the last clean
whole-suite result applies to the earlier search-fix source state. The overall
migration and US-020 remain partial. Exact evidence is in `status.json` under
`angularAccountingAliasDimensionReview`.

## Angular global-search submit deduplication

Typing and submitting the same term could dispatch duplicate search reads when
Enter arrived near the typeahead debounce. The UI now cancels the pending timer
synchronously and guards repeated requests for the active term; a failed or
unsupported response clears the guard so the user can retry. The regression
journey asserts exactly one search request for fill plus Enter and covers both
canonical and `/ui` routing.

The Release solution build passed with zero warnings or errors, all 451 Angular
tests across 87 files passed, the production UI build passed, and both focused
Playwright route journeys passed. Gitleaks reported no staged leaks. The full
solution suite and EF drift check were not rerun for this UI-only slice; the
previous integrated full-suite attempt remains interrupted, and the overall
Angular migration, cutover and Blazor retirement remain open. Exact evidence is
in `status.json` under `angularGlobalSearchSubmissionDeduplication`.

## Integrated regression attempt on the current master

The Release solution build passed with zero warnings or errors after the
assessment-timeline and resource-grid changes. A fresh full solution test then
remained in the API test stage for more than twelve minutes without a summary;
the API host entered an idle wait and was interrupted. No API, Domain, E2E or
aggregate test counts are inferred from that attempt. The prior
`verifiedCommit` remains the last successful full-suite checkpoint. Details are
recorded under `integratedFullRegressionAttempt575c338` in `status.json`.

## Angular resource-grid range integrity

The grid now derives week headers from the server-returned normalized range, so
an empty staff list retains its selected week columns. The empty row spans the
full table width. Angular validates the 1–12 week bound and verifies that every
staff row and allocation aligns with the returned date range; malformed grids
fail closed.

All 451 Angular tests across 87 files, the production UI build, and both
canonical and `/ui` resource-planning browser journeys passed. The focused unit
case covers three weeks with no staff and checks the six-column empty row. The
full .NET suite and EF drift check were not rerun; wider planning acceptance and
Blazor retirement remain open. Evidence is in `status.json` under
`angularResourceGridRangeIntegrity`.

## Angular assessment specialist timeline

The assessment workspace now reads the latest 200 specialist request/result
events from immutable, actor-owned command receipts, scoped to the authorized
firm and client. It validates receipt identity against the retained specialist
review record and displays action, area, specialist, status, actor, time,
evidence reference and conditions. Current reviews also show their request and
clearance timestamps. No migration was needed.

The Release solution build, all 449 Angular unit tests, production Angular build,
focused PostgreSQL API projection test and both canonical and `/ui` Playwright
journeys passed. The new browser journey displayed a synthetic requested review
and HOLD result. The full .NET suite and EF drift check were not run for this
slice; the broader migration and retirement gates remain open. Exact evidence is
in `status.json` under `angularAssessmentSpecialistTimeline`.

## Angular SharePoint administration tab-draft recovery

Selected SharePoint resource and versioned folder-template forms now support
explicit bounded tab drafts. Recovery is tied to the current setup/resource or
template-catalog basis, restores editable values only, and always clears review
assent. The tenant connection route composes both setup and SharePoint guards;
refresh and unrelated writes ask before they can discard dirty fields. Same-tab
browser recovery was verified and created no template record.

The Angular suite (448 tests across 87 files), production build, Release solution
build, and focused SharePoint Playwright journey passed. The fresh whole-solution
test was stopped after more than twelve minutes without a test summary while the
API test host remained CPU-active in JIT compilation; it is not a pass. Exact
evidence and remaining migration boundaries are in `status.json`. This does not
close full migration, production cutover or Blazor retirement acceptance.

## Angular embedded commercial form draft recovery

Proposal, quotation, commercial-document and fee-agreement fields now have
explicit bounded tab drafts tied to current identity, session and authorized
workspace state. Recovery excludes assent, quotation previews, executable
requests and uploaded files. Proposal navigation checks all embedded editors
and refuses unresolved command outcomes. A successful proposal refresh retains
unsubmitted revision fields; a refused refresh clears protected content.

Angular tests and an isolated UI build passed as recorded in status.json.
Built-in browser recovery and three affected E2E journeys passed. Angular tests
and the isolated UI build passed as recorded in status.json. The captured whole
regression is terminal: API and Domain passed, while one E2E assertion failed
because its exact-text selector matched the same denial message twice. The
selector was corrected and its affected journey passed; the frozen artifact
manifest matched all 1,350 files. A whole current-source rerun remains open.

## Angular commercial settings draft recovery

Firm letterhead and approval-rule edits now have explicit bounded tab drafts,
identity/session and profile-version/rules-fingerprint binding, fresh authorized
recovery, unsaved-navigation protection and unload warnings. Drafts exclude
profile revision and review assent; recovered fields use the currently loaded
version. Editing fields invalidates their review confirmation. Saving one form
retains unsubmitted edits in the other form.

The Angular suite, isolated build and built-in synthetic browser journey passed;
results are in status.json. This closes pre-dispatch settings recovery only.
Quotation/document/fee-agreement drafts and durable commercial command recovery
remain open. The running regression retains its captured executable/UI assets;
these later settings changes are verified separately, and source guards may read
the current working tree.

## Angular firm safety and runtime parity

Administration now includes the persisted firm safety mode and exact deployment
epoch, allowlisted hosting environment, external-effects flag and simulation
adapter flag. A narrow Application query requires current firm-wide Administrator
authority before and after reading. The API supplies only selected typed flags;
no configuration dump or credential references reach the browser.

Missing safety state remains unknown rather than healthy. Recovery quarantine
explains the halt and links to operations; liveness and readiness links remain
separate from Microsoft/provider acceptance. Local builds, the Angular suite,
PostgreSQL/API tests and built-in browser verification passed as recorded in
status.json. The previous E2E recovery has now finished successfully, but its
artifact drift still prevents frozen-checkpoint attribution.

## Angular tenant setup acceptance extension

Automated API coverage now verifies exact reviewed metadata, CSRF and current
session authority, guessed identities, altered intents, replay, immutable event
storage and retained-evidence downgrade refusal. The new browser journey reloads
after publication and explicitly recovers the committed receipt without issuing
another mutation. Lost-response unit coverage preserves pending references and
prevents resend. Exact review receives keyboard focus.

The full Angular suite and affected browser/API journeys passed, with results in
status.json. Owned browser hosts now support isolated Release artifacts and an
explicit validated repository root, so concurrent runs need not replace each
other's executable files. Default host behavior is preserved. Full migration
regression, canary and retirement remain separate unfinished gates.

## Angular tenant setup editor

Safe tenant label and mail/records configuration flags now have a native Signal
Forms editor backed by Application preview, save and actor-owned receipt lookup.
Exact intent and current setup state are reviewed before publication. Current
firm-wide administrator authority, configured tenant binding, protected-state
checks, revision fencing and immutable evidence are enforced server-side.

Unsubmitted tab drafts retain only bounded local fields, never assent. Pending
submissions retain request references for explicit receipt reconciliation, never
automatic replay. Navigation protection and session-bound late-response checks
preserve unsaved edits and prevent stale responses from applying.

Focused builds, PostgreSQL tests, Angular tests, EF drift inspection and built-in
browser verification passed; evidence is recorded in status.json. The migration
was applied only through owned synthetic test schemas. The later acceptance extension above closes automated setup mutation journeys
and downgrade safeguards; wider migration acceptance remains open.

A later artifact check found drift in the original regression manifest. Its
running E2E recovery cannot establish the original frozen checkpoint. Earlier
API/Domain terminal results remain observed, but aggregate completion is not
claimed. The older recovery section below describes the initial restart only.

## Angular full regression recovery

The full API and Domain suites passed on the frozen migration checkpoint. User
interruption ended the original process before an E2E completion result was
recorded. After confirming the old process absent and the artifact manifest
unchanged, the unfinished E2E suite was restarted against the existing Release
binaries and UI assets. Its terminal result remains pending in status.json.

Concurrent commercial source edits and a new performance test are preserved and
are outside this compiled checkpoint. Aggregate regression completion, wider
migration acceptance and final Blazor retirement remain unverified. No shared
Development or production database migration was performed.

## Angular Microsoft tenant setup metadata

The tenant workspace now projects the saved friendly tenant label and mail/records
setup states through the current firm-wide administrator query. Angular displays
these in a separate saved-configuration section, explaining that they do not prove
consent, mail transport, document protection or release readiness. Immutable tenant
identity and independent capability verification remain unchanged. Unsupported
setup states fail decoding, absent metadata adds no invented state, and session
loss removes the protected metadata.

Local build, contract, PostgreSQL and browser evidence is recorded in status.json.
Built-in browser inspection used an owned synthetic database and observed saved
mail configuration alongside an independently disabled mail capability. No shared
Development database change or Microsoft operation was performed. Setup field
editing/recovery, wider administration parity, full regression and final migration
acceptance remain open.

## Angular reviewed resource planning and retained receipts

Profile, certification, availability and allocation commands now require an exact
server preview and explicit assent. Current authorization, enabled staff identity,
capacity, availability and staffing are checked under publication locks. Changed
review inputs refuse publication. Mutation and immutable request receipt commit
together; matching retries return the retained result without another mutation.
Receipt lookup waits for concurrent publication and remains actor/firm scoped.

Angular retains only a bounded request reference for reload recovery. Lost or
unverifiable responses fence further commands until lookup and acknowledgement;
no command is retried automatically. Unsubmitted edits remain in memory and other
forms survive acknowledgement. PostgreSQL guards reject receipt changes and
rollback over retained evidence.

Release and production UI builds, Angular tests, focused Domain/API tests and
canonical/preview browser journeys passed. Built-in browser verification exercised
preview, assent, receipt and acknowledgement without console warnings/errors.
Exact metrics and logs are in status.json. Only owned test databases were migrated;
shared Development and production were not migrated. No external provider action
was performed. Wider staffing/budget parity, full regression, cutover and Blazor
retirement remain open.

## Angular resource planning forms and keyboard parity

Resource planning now uses typed Signal Forms for staff profiles, certifications,
unavailability and weekly allocations. Bounded percentages/hours, real dates,
ordered absence ranges and currently offered identities are validated before
submission. Existing profiles populate the editor; persisted decimal percentages
remain editable. Accessible inline errors and a focusable error summary support
keyboard submission. The grid separately displays capacity, planned and approved
actual utilization, over-allocation and recorded engagement allocation details.

Independent unsubmitted forms survive another form's save. Leaving or refreshing
asks before discarding memory-only edits; session loss clears protected data and
fences late command results. Unknown additive outcomes block repeat submission
and explain the need for reconciliation rather than silently creating duplicates.
An allocation explicitly does not create authorization grants or change SharePoint
permissions. Existing Application authorization and calculations remain authoritative.

Production UI/Release builds, the Angular suite, focused PostgreSQL planning tests
and both canonical/preview browser journeys passed. Built-in browser verification
exercised synthetic profile save/reload/save with no console warnings/errors.
Observed metrics are in status.json. This does not complete planning acceptance:
the later reviewed-command slice supersedes its stale allocation and receipt
recovery gaps. Wider migration parity, full regression and cutover remain open.
No shared Development database migration or external provider operation was run.

## Angular assessment review and recovery forms

Native assessment actions now use typed Signal Forms with bounded validation,
accessible error summaries and exact server previews. Explicit assent applies only
to the current preview; cancelling or editing invalidates it. All five actions
compose the reviewed Application command and immutable receipt endpoints.

Only a dispatched request ID and hash are retained in tab storage. Answers,
evidence, professional notes and assent remain in memory. Lost responses and
reloads fence further actions until authorized receipt lookup and explicit
acknowledgement; no action is automatically retried. Unavailable storage prevents
dispatch. Navigation asks before discarding unsubmitted edits and blocks unresolved
requests. Historical and engagement-specific decisions remain read-only.

Production UI, Release build, Angular tests and focused browser checks passed.
The built-in browser verified a synthetic answer review, committed receipt and
refreshed persisted progress with no console warnings/errors. Focused backend and
model checks are recorded in status.json. Shared Development migration and live
Microsoft acceptance have not been performed. Broader assessment parity,
resource-grid acceptance and migration cutover/Blazor retirement remain open.

## Reviewed assessment commands and backend recovery

The Application assessment workspace now previews and executes answer revisions,
specialist review requests/results, Partner decisions and continuance through the
existing capability services. An exact review basis binds persisted evaluation,
question definitions, answers, effective clearances, target evidence and current
actor authority. Explicit assent is required; acceptance still requires the
existing professional readiness gates. Irrelevant or oversized action fields are
refused. No engagement is activated by these commands.

Execution holds firm, client and actor locks and publishes the local mutation and
append-only request receipt in one transaction. A matching committed receipt is
returned before checking changed evaluation state; changed intent cannot reuse
the request. Authorized actor-owned lookup waits for in-flight publication before
reporting absence. No timeout establishes failure and no unknown action is blindly
retried. Database guards validate current scoped authority and the exact result;
retained receipts prohibit migration rollback. Specialist request reuse selects
the same deterministic open review shown by the preview.

The new migration is exercised only in disposable PostgreSQL test schemas and
databases. Shared Development or production migration has not been performed.
Focused verification evidence is recorded in `status.json`; whole-suite and live
Microsoft acceptance remain separate. Existing endpoints stay available for
retained builds. Angular still needs typed forms, explicit preview/assent,
retained receipt references and navigation/recovery integration before this native
workflow is complete. Resource-grid parity, wider acceptance and Blazor retirement
remain open.

## Assessment command transaction ownership and current authority

Answer capture, specialist review requests/results, Partner decisions and
continuance now join a caller transaction when one exists. Standalone calls
retain their own commit/rollback boundary. A reviewed caller must roll back its
entire scope when a composed command fails; this enables the upcoming mutation
and immutable receipt to publish together.

Commands acquire firm, client and actor publication locks in that order, then
recheck persisted authority after waiting and again before publication. Owned
PostgreSQL tests cover rollback without answers, reviews, decisions, workspace
intent or a new continuance generation, plus revocation during each command's
firm-lock wait. Existing human decision, scope, generation and protected-state
rules remain enforced. The focused API and legacy/native browser checks remain
separate from whole-suite acceptance; observed results are in `status.json`.

Next: immutable reviewed assessment request receipts and authorized recovery,
then typed Signal Forms and navigation/draft parity. No receipt UI or persistent
request recovery is claimed by this transaction foundation.

## Native assessment detail and exact legacy selection

The Angular assessment now composes a named Application projection for client
identity, relationship status, selected professional decision, repository state
and persisted overall/section progress. Legacy decision-ID links retain their
exact selection. Historical and engagement-specific decisions remain read-only;
opening the current client evaluation does not substitute an old decision.
Pending status is not presented as a recorded professional conclusion.

Reads hold firm, client and actor publication locks, enforce current client
scope, and omit remote resource locations and provider diagnostics. Unknown,
foreign and revoked selections fail closed. Native decoding bounds generation,
revision, identity and progress; failed reads clear protected edits. Partner
confirmation is memory-only and tied to the exact evaluation and decision fields.
Changing reviewed fields clears assent; stale callbacks cannot change a new
route's busy state.

Focused local build, unit, PostgreSQL API and browser evidence is recorded in
`status.json`. Built-in verification used an owned disposable fixture. Remaining
assessment work includes typed Signal Forms, retained command receipts/recovery,
specialist timeline detail and broader navigation/accessibility acceptance. The
whole migration, resource grid and production cutover/retirement remain partial.

The earlier conversion whole regression was cancelled after concurrent source
and Release assembly changes invalidated its frozen manifest. Partial project
passes are retained separately; they do not replace the whole-suite checkpoint.

## Reviewed prospect-to-client conversion

The native proposal now links to an explicit Angular conversion review. The
workflow captures bounded legal identity fields, shows canonical-client reuse,
primary contact and pending portal intent, and requires fresh assent bound to the
exact server preview and current actor/proposal context. Restricted-profile text
is memory-only before submission and excluded from editable tab drafts.

Application execution composes the existing commercial conversion inside one
transaction with an append-only actor-owned receipt. Firm serialization precedes
proposal reads. Existing client metadata remains unchanged; stale contact/client
context and conflicting canonical identities fail closed. Unknown replies retain
a request reference and use authorized receipt lookup rather than retrying POST.
Conversion grants no portal access and does not activate an engagement or bypass
professional acceptance. The legacy rollback surface remains available.

Focused Angular, commercial/domain, API and canonical/preview browser checks
passed, as did Release/production builds and model-drift verification. The
built-in browser verified an owned synthetic conversion and acknowledgement;
its host/database were disposed. Exact evidence and recovered environment errors
are in `status.json`. Whole migration acceptance remains partial.

## Joined master regression checkpoint

The frozen whole-solution parent completed successfully. Domain, API and browser
projects all passed without failures or skips; the recorded source/assets/assembly
manifest matched after completion. Exact source revision and metrics are in
`status.json`. Later documentation and CI-only changes are separate from that
application checkpoint. This is local verification, not production or Microsoft
provider acceptance. Reviewed prospect-conversion drafts have been checked in
isolated temporary trees and are being integrated next; migration remains partial.

## Native reviewed staffing and receipt recovery

Angular staffing assignment and revocation now request an exact server preview,
then require separate assent bound to the full preview, current actor/session,
engagement and read context. The preview explains engagement role scope, current
certification, local grant/session effects and separate client-site reconciliation.
Unsupported replies fail closed. Competing edits/navigation are fenced during
review or unknown writes. The team projection hides stale content until receipt
verification and acknowledgement refresh current authorized state.

A bounded tab reference is saved before dispatch. Lost responses and reload use
actor-owned receipt lookup; no mutation is automatically retried. Protected state
clears on context loss and late callbacks cannot affect a different owner.
Focused Angular, API and canonical/preview browser journeys passed. Built-in
synthetic verification covered assignment and revocation receipts and refreshed
team state without console warnings/errors. Hosts and tabs were disposed. Detailed
counts and logs are in status.json; the full regression remains separately tracked.

Client creation follows accepted-proposal conversion in the existing architecture;
it must preserve commercial and professional acceptance boundaries. Reviewed
conversion/draft recovery, assessment parity, remaining module acceptance and
quality/cutover/retirement remain open. No live Microsoft effects or shared database
migration was performed by this UI slice.

## Reviewed staffing backend receipts

Application staffing preview now binds the exact action, target identity, level,
assignment, certification, local grant effects, client/engagement generations and
current actor/session. Assignment review states the owner-approved entire-client-
site Full Control consequence; Microsoft membership still reconciles separately.
Confirmed execution composes the existing staffing service inside one guarded
transaction and retains an immutable actor-owned receipt. Exact request replay
returns the original receipt; changed intent and stale review fail closed.
Receipt lookup uses a publication barrier before reporting absence.

The new persistence migration adds append-only evidence and a deferred assignment/
revocation linkage guard, with rollback refusal when retained evidence exists.
It is exercised only in disposable test schemas, not shared Development or
production. Observed build and focused PostgreSQL evidence is recorded in
status.json. Angular staffing still uses its existing command path; wiring native
preview/receipt recovery and its API/browser acceptance is the next migration work.
No complete migration or live Microsoft acceptance is claimed.

## Staffing transaction composition and publication fencing

Staffing assignment and revocation now join a caller-owned transaction, or own and
commit their transaction when called directly. A caller receiving refusal must
roll back its transaction. Firm/client safety locks serialize publication; locked
current actor authority and rank are checked again before mutation, and current
authority is checked before successful publication. Target identity locks protect
grant and session-epoch changes. Existing certification, exact scope, independent
grant preservation and selected-site membership reconciliation remain unchanged.

This is the transaction foundation for reviewed staffing receipts. Reviewed
staffing/revocation preview, request receipts, unknown-outcome UI recovery and wider
migration acceptance remain pending. Observed build/test evidence belongs in
status.json. No database migration or live Microsoft operation is introduced.

## Reviewed budget approval and retained recovery

Independent approval now previews exact draft lines, currency, version and client/
engagement generations. A separate confirmation binds the complete preview to the
current actor and session. Application execution composes the existing approval
policy and transaction, retains an immutable actor-owned receipt, and refuses
self-approval, stale review or changed intent. Unknown writes require explicit
receipt verification; reload never resends approval. Acknowledgement refreshes the
current approved projection rather than displaying an old draft as current.

Focused Angular, PostgreSQL, API and browser checks passed, together with Release
and production UI builds and clean EF model drift. Built-in synthetic browser
verification covered review, receipt and acknowledgement. Source and assemblies
remained frozen during checks; all consumer parents joined successfully. Detailed
counts, logs, source identity and retained earlier build failures are in status.json.
The new migration was exercised only on owned disposable databases: empty rollback/
reapply passed and retained evidence correctly prevented rollback.

Staffing/revocation request recovery, client creation, assessment parity, a new
whole regression and migration quality/cutover/retirement remain open. No shared
Development migration, live Microsoft or production acceptance is claimed.

## Planning action-specific authority

The planning projection exposes reviewed budget-preparation authority separately
from staffing authority, reusing the Application preparation policy. Angular
preparation fields, draft persistence, preview/confirmation and retained receipt
reconciliation use that permission and reject missing contract flags. Draft
approval remains a separate section with its existing independent backend policy;
Administrator-only staffing does not expose the preparation editor.

Release and production UI builds, Angular tests, affected PostgreSQL tests and
canonical/preview browser journeys passed. Built-in synthetic previews verified
Manager access and Administrator-only denial of preparation. EF reports no model
drift. Source, production assets and Release assemblies stayed frozen during
verification; preview hosts and tabs were disposed. The earlier editor-fixture
failure is retained in the ledger; explicit scoped Manager grants now represent
editor authority, and separate Administrator-only journeys preserve the denial.

Reviewed staffing/revocation and budget-approval request recovery remain open,
along with client creation, assessment parity and migration quality/cutover gates.
No full migration, live Microsoft or production acceptance is established.

## Staffing grant expiry enforcement

Staffing rank now uses current Application authorization, including exact scope,
grant expiry and session identity. An active lower role cannot revive an expired
higher role. Staffing replacement first retires expired target grants through the
existing expiry service, preserving append-only evidence and session invalidation
before creating a fresh engagement grant. Unexpired independently assigned grants
remain reusable.

The focused Release build and affected PostgreSQL tests passed. Earlier fixture
and active-grant uniqueness failures are retained in the execution ledger. This
changes no UI or schema and does not close reviewed staffing/revocation recovery,
budget approval recovery, action-specific UI authority, or migration cutover.

## Planning Signal Forms and exact-context assent

All planning controls now use Signal Forms. Staffing assent binds the current
identity, engagement, read lifetime, person and level. Draft-preparation assent
binds the exact preview, and approval assent binds the exact displayed draft.
Immediate command checks refuse stale assent before rendering catches up; public
field reset synchronizes native checkboxes. Staffing selection, preparation and
approval stay disabled during unknown writes, and revocation respects the shared
review fence. Existing Application authorization and commands remain the authority.

Select labels now use explicit association so their names exclude option text.
Observed production/Release builds, the Angular suite and affected canonical/preview
browser journeys passed. The built-in browser verified exact labels and native
checkbox clearing in an owned synthetic preview, now disposed. Initial test failures
and their correction are retained in `status.json`; assertions were preserved.

Backend reviewed staffing/revocation and budget-approval request recovery and
individual action authority projection remain open, followed by client creation,
assessment parity and migration quality/cutover/retirement. No schema or live
Microsoft changes were made; these focused results do not replace the earlier
whole-suite source boundary.

## Planning navigation and editable-field preservation

Engagement navigation now guards unsaved planning edits with keep, budget-only tab
save, or explicit discard. Staffing choices and approval assent are never treated
as saved budget fields. Failed storage keeps the editor open. Current identity,
engagement lifetime and authority fence late dialog results. Busy or unknown writes
must be reconciled before internal departure; tab departure warns without trapping
sign-out. Same-engagement hold paging preserves the editor without opening a dialog.

The production UI build, Release test-project build, Angular suite and affected
canonical/preview browser journeys completed successfully. Built-in browser checks
confirmed keep/save/return/restore in an owned synthetic preview, now disposed.
Detailed observed evidence and the prior whole-regression boundary are in
`status.json`. No schema or live Microsoft changes were made. Staffing/approval
request recovery, remaining planning forms and wider migration acceptance stay open.

## Native budget Signal Forms editor

Budget inputs now use Signal Forms with bounded field metadata and whole-minute
validation. Line changes replace the model immutably. Invalid touched fields show
an accessible validation summary. Review and unknown-outcome fences disable the
fields and tab-draft actions; explicit assent and immutable receipt recovery keep
their existing behavior. Approval remains a separate action.

The production UI build, Angular suite and affected budget/profile browser journeys
completed successfully. The built-in browser checked line changes, validation,
correction, exact rate review and disabled confirmation before assent in an owned
synthetic preview, which was disposed. See `status.json` for observed evidence.
No database schema or live Microsoft change was made. Staffing/approval request
parity, other planning forms, navigation guards and wider migration gates remain
open.

## Reviewed native budget preparation and retained request recovery

The planning UI now previews exact approved rates and calculated forecast values
before explicit draft-preparation assent. The Application workspace reuses the
existing budget service inside one guarded transaction. A new draft and its
append-only actor-owned request receipt commit together; a deferred database guard
checks the exact publication and current authority. A changed revision or rate
snapshot requires another review. Existing budget endpoints remain for older builds.

Before dispatch the UI saves only the request reference in tab storage. Unknown
responses fence additional changes; reload reads fresh current scope and reconciles
the retained receipt without resending. Acknowledgement clears recovery state.
Budget approval stays separate and requires a different authorized practitioner.
Projection refresh drops approval assent. Scope loss and destruction clear protected
review fields and late callbacks cannot restore a prior route visit.

Focused PostgreSQL, API, Angular and browser evidence is in `status.json`, including
the corrected initial SQL-schema mismatch. The built-in browser checked the receipt
flow with disposable synthetic records, followed by final acknowledgement-copy
regression checks. The additive migration was exercised only in owned test/preview
databases; no shared deployment or Microsoft effect occurred. The whole-suite run
on the earlier creation source has joined successfully and its frozen fingerprints
match. It establishes that earlier checkpoint only; this newer slice remains
separately verified with focused evidence.

Next: staffing/approval request parity, remaining planning Signal Forms, client
creation, assessment parity and migration quality/cutover/retirement acceptance.
The overall migration remains partial.

## Native planning editable budget drafts

Budget preparation reuses the tab draft service for explicit save, restore and
discard. Draft identity follows the exact engagement, firm, actor and session;
restoration requires the current authorized budget version and draft state.
Only bounded editable fields are stored: no approval assent, calculated cost,
request execution or professional acceptance is restored. Unknown command outcomes
fence these controls. Refused access clears protected fields.

Angular tests, production build and the built-in browser verified this local slice;
observed evidence is in `status.json`. Owned synthetic preview hosts and database
were disposed. Reviewed Signal Forms and server request receipt recovery remain
open, together with client creation, assessment parity and migration cutover gates.

## Planning protected editor clearing

Native planning now removes protected editable fields and review assent immediately
when a command reports unavailable access, and revalidates the scoped projection.
Malformed refreshed projections also clear protected editor content. Focused
component checks cover both cases; observed test and build results are in
`status.json`. Reviewed planning forms, revision-bound drafts and retained request
recovery remain open. The separate frozen creation regression has joined successfully with matching
fingerprints; its source boundary remains recorded in `status.json`.

## Reviewed native blocked-engagement creation

The client profile now links to a focused Signal Forms creation route. Current
client-wide Partner/Manager authority is required; engagement-only scope and
Administrator authority alone do not suffice. The server preview binds the exact
client revision, service, profile and period, and confirmation requires fresh
explicit review. Existing service-period shells require inspection. New shells
remain Draft with professional work blocked; activation remains independent.

Creation and immutable actor-owned request evidence commit together under the
client safety lock. Current authority is checked after locking and before commit.
A deferred database guard binds the evidence to the exact blocked shell, actor and
client revision. Same-intent retries retain the original receipt. Lookup waits for
an in-flight transaction before reporting absence. The additive migration permits
empty rollback/reapply but refuses rollback while creation evidence exists. Only
owned disposable databases were migrated.

Editable drafts are explicit, bounded, tab-only and revision-bound; they exclude
review assent. Unknown responses fence resubmission and navigation until receipt
reconciliation. An absent receipt permits fresh review of the identical request;
changed intent cannot reuse it. Denied commands and failed receipt reads remove
protected context and require a new authorized read. Route changes, session loss
and destruction fence late callbacks. The older API creation endpoint and Blazor
presentation remain compatible for rollback.

Focused Release, Angular, PostgreSQL, API, browser and model checks passed. The
built-in browser verified exact review, keyboard confirmation, immutable receipt,
mobile bounds and sign-out clearing using disposable synthetic data. Initial
compile and encoded-route corrections, final joined build fingerprints and exact
counts live in `status.json`. This does not complete wider migration acceptance.

## Reviewed native Partner activation and response recovery

The Angular engagement page now links to a focused Partner review route. Its
Application context displays exact service/profile/period metadata, engagement and
client revisions, current same-service acceptance and active holds. Administrator
authority alone cannot activate. A fresh explicit review is required before a
current unconditional acceptance and zero holds can release a draft engagement.

The existing activation command now composes an owned transaction without cached
safety/engagement state. Activation, the guarded revision change, existing portal
intent handling and immutable native request evidence commit together. Current
Partner scope, session epoch, acceptance and holds are rechecked. A retry returns
the original actor-owned receipt, and a late revocation rolls back the activation
and engagement together. The additive migration preserves legacy rows, guards
native publication at commit and refuses rollback while reviewed evidence exists.
It has been exercised only in owned disposable databases.

The native Signal Forms page stores no review assent. Tab recovery retains only
request identity; unknown responses fence new submission and navigation. Receipt
lookup waits behind any guarded activation transaction. An absent result permits
fresh inspection and a new explicit review of that same fixed action. Scope/session
loss, destruction and a return to the same route fence late callbacks. The retained
legacy activation endpoint remains available for older UI builds.

The built-in browser checked exact review, keyboard confirmation, the receipt,
responsive layout and sign-out clearing. Synthetic local acceptance is not a
professional or Microsoft acceptance. Focused checks, intermediate compile/route
test corrections and immutable build fingerprints are recorded in `status.json`.
The earlier contact-commit whole run has terminated with its retained-evidence
rollback-target and concurrent browser diagnostics test failures; both focused
repairs preserve their original safety assertions. It does not replace the last
successful whole-suite `verifiedCommit`.

The published activation source has now passed a whole regression in its separate
frozen worktree. Every cohort and the parent joined successfully, and source,
Angular asset and Release assembly fingerprints match their pre-run manifest.
This updates the whole-suite `verifiedCommit` for activation source only. The newer
reviewed creation slice has its own focused evidence. Its fresh whole run is now
running in a separate frozen worktree; no result is inferred before parent completion
and fingerprint comparison.
Exact source identities, observed counts and runtime evidence live in `status.json`.

Next: reviewed planning forms/drafts/recovery, client creation,
remaining assessment/source-action parity, wider quality and production-like
cutover/rollback acceptance, then Blazor retirement. The full migration remains
partial. The existing operator migration workflow still applies; no Wiki update,
shared Development migration or external Microsoft effect was performed.

## Native engagement metadata, clearance and hold history

The scoped Application projection now retains service-profile and UTC creation
metadata, exact revision strings, complete active/released hold totals and stable
bounded history pages. Client-profile navigation requires separate CLIENT authority.
Active holds display blocked professional work independently from the stored flag;
the clearance link leads to retained gate evidence. Older native builds retain the
unpaged bounded endpoint contract.

Same-engagement history revalidation hides protected content while preserving the
team/budget editor and entered values. A refusal, mismatched response, route or
session change removes it. Route lifetime fences stop callbacks from an earlier
visit, including a return to the same identity, from changing current busy state or
review. Unsupported activation acknowledgments remain unknown and cannot authorize
another blind submission.

The built-in browser checked metadata, responsive layout, page-size changes,
preserved edits and sign-out clearing in an owned synthetic database. The focused
PostgreSQL/API/Angular and corrected browser results, original failed attempts and
artifact fingerprints are recorded only in `status.json`. An existing browser
scope assertion now uses a thread-safe diagnostics collection; its isolation
assertions remain intact. The separate whole run exposed that earlier collection
race and cannot establish whole-suite acceptance.

Reviewed creation, activation and planning forms, scoped drafts and retained
request recovery remain the next eligible work. Full migration quality, production
cutover and Blazor retirement remain open. No shared migration, live Microsoft
operation or Wiki publication was performed.

## Contact publication and engagement parity continuation

The reviewed contact step is pushed to remote master; its publication evidence is
recorded in `status.json`. A curated engagement source audit now identifies missing
master metadata, complete hold counts/history, client-profile navigation authority
and late activation callback lifetime fences. Planning edits must survive ordinary
same-engagement history paging and clear when current authority is lost. The
[engagement action audit](angular-engagement-parity.json) distinguishes existing
commands from remaining form/draft/outcome acceptance.

The earlier full regression terminated with the known navigation-selector failures;
its source/assets/assemblies matched after every consumer joined. Those selectors
are corrected and pass on the published client/contact steps. A fresh full run on
the contact commit now consumes a separate clean, rebuilt and frozen worktree.
Exact outcomes and test/build/hash paths remain only in `status.json`. The latest
full successful acceptance remains the top-level `verifiedCommit`; an in-flight
rerun does not replace it.

Native engagement parity is the active next slice. Full migration, production
cutover, wider quality gates and Blazor retirement remain open.

## Native reviewed client contact creation

A focused native contact route replaces the inline Angular contact form. The
Application preview binds the current client safety revision and exact editable
intent. Confirmation shows any primary contacts being replaced and requires fresh
assent. Contact creation continues to grant no portal or Microsoft access.

The existing contact command now composes an owned transaction, reads locked
safety state without cached tracking, increments the revision and rechecks current
staff authority before commit. The reviewed workflow saves the contact and an
actor-owned append-only receipt together. Composite constraints bind its firm,
client, contact and actor. Database guards reject receipt mutation, inconsistent
publication and rollback while evidence remains.

Explicit tab drafts contain bounded editable fields only. Pending recovery retains
an immutable request identity across changed revisions, never stale fields or
assent. Lost responses fence new submissions until receipt verification; an absent
receipt permits fresh review of the identical original intent with the same
request. A changed intent cannot reuse it. Returning client/profile routes refetch
current authority. Retained older UI builds keep their existing contact endpoint.

Release, Angular, PostgreSQL, API, browser and model checks passed. The built-in
browser displayed a synthetic creation receipt, responsive mobile state and
sign-out content removal. All owned slice consumers joined, their source/assets/
assemblies still matched, and temporary hosts/tabs were disposed. Exact outcomes,
intermediate corrections and artifact paths belong to `status.json`.

The earlier whole regression remains frozen on its own commit with known
navigation-selector failures; it has no terminal whole-suite acceptance yet.
Engagement reviewed forms/drafts, remaining source-action parity, production
cutover and Blazor retirement remain open. No shared database migration or external
Microsoft action was performed. The existing operator migration workflow remains
applicable; no Wiki publication was made.

## Native client profile metadata and complete scoped pages

The client projection now preserves source master metadata, exact safety generation,
UTC onboarding time, complete authorized summary counts and independent bounded
contact/engagement pages. Restricted profile content is excluded. Client-level
commercial access never widens an engagement assignment; every contributing scope
is rechecked before delivery. Native links and refresh keep the existing command
and assessment authorities.

Validated page parameters survive reload. Superseded reads, mismatched identities
or page responses, session changes and old command callbacks cannot refill earlier
content. The existing portal-intent notice explains acceptance and invitation gates
without granting access or claiming Microsoft redemption. Existing contact and
blocked-engagement commands remain; full reviewed forms, drafts and command-outcome
recovery are the next client work.

Release and Angular builds/unit checks passed. API checks and built-in desktop/mobile
paging, reload and sign-out checks passed. Final focused PostgreSQL/browser checks passed after correcting the expired-grant
fixture while preserving its database constraint. Unpaged API requests retain the
original bounded window for older UI builds; the new view requests explicit pages.
Final source, assets and assemblies match their frozen manifest. Observed results
and intermediate failures belong in `status.json`. The isolated earlier portfolio whole-suite run has exposed
a shell selector matching both navigation and the configuration notice. The focused
selector is corrected in this slice; the earlier frozen run remains unchanged and
cannot support a whole-suite pass claim. Full migration acceptance remains partial.

## Recent portfolio table controls and frozen regression result

Candidate and package panels now independently page the already authorized recent
window with the preserved row-size choices. Presentation changes fetch no extra
records, cannot enlarge scope and do not change CSV semantics. Search/session changes
reset the table pages; empty or shortened windows clamp safely. Angular checks and
builds passed. The affected browser cohort passed after synchronizing the existing
sign-out test with its asynchronous cookie response; content removal and API denial
assertions remain. Built-in desktop/mobile checks passed against frozen assets.

The earlier frozen canonical whole-solution regression has completed successfully.
Source, production UI assets and assemblies matched before the owned worktree was
reused. Its source identity, terminal results and retained evidence are recorded in
`status.json`. Responsive shell and portfolio slices have separate newer evidence;
they are excluded from the earlier whole-suite claim. Full client/engagement parity,
quality gates, production cutover and retirement acceptance remain open.

## Native scoped portfolio summary, records and export

The Application projection now owns summary counts, bounded recent candidates and
financial packages, and a formula-safe scoped CSV. Every section uses one authorized
scope snapshot and rechecks all contributing grants before delivery. Client and
engagement relationships remain explicit; mid-read revocation refuses the whole
projection even when another valid grant remains. Export validates antiforgery and
refuses an oversized client set instead of silently truncating it.

The native view preserves refresh, operational shortcuts, candidate/package links,
creation timestamps, client page-size choices and current-search states. Validated
URL parameters and memory-only exact-owner location restore filters and selected
client on return; no protected results are cached. Session, route and filter changes
fence late reads and downloads. The saved-configuration notice establishes no
Microsoft capability verification.

Release, Angular, focused PostgreSQL/API and model checks passed. The first browser
cohort failed only at an exact synthetic client-name selector; the corrected
cohort passed against frozen assets. Built-in desktop/mobile checks preserve scope,
return navigation, page size, restricted client routing and sign-out removal.
Observed counts, logs, intermediate bundle warnings and final repair are recorded
only in `status.json`. Recent-table pager, broader client/engagement source-action
and whole migration acceptance remain open. The isolated earlier canonical whole
run has completed API/Domain cohorts and is still waiting for browser completion.

## Responsive Angular navigation and source bookmark parity

The native desktop and compact shells now share their presentation links. A compact
Material modal provides keyboard focus containment, Escape/backdrop/close recovery,
successful-navigation destination focus and current-session ownership. Canceled guards
preserve the menu. Resize and late lazy-load fences prevent hidden focus or overlays
from returning after revocation, layout change or destruction. Portal links remain
restricted to the client workspace; API authorization remains unchanged.

The preserved Microsoft 365 bookmark redirects to the guarded tenant workspace.
A contract checks every legacy workspace route against explicit native host/router
ownership. Root/authentication denial remain server endpoints. Route coverage does
not establish complete source-action or retirement acceptance.

Build, Angular/API and final affected browser cohorts passed; built-in browser checks
cover keyboard entry/recovery, destination/resize focus and staff/client narrow views.
Earlier failing selector/resize runs and the actual trigger-focus repair are preserved
in `status.json`. Final source/assets/assemblies remained frozen and match. The broader
whole-solution run is isolated on the earlier canonical checkpoint and remains running.

Next eligible source gap: native portfolio summary, release/package panels and scoped
CSV export. Full action parity and wider accessibility/performance acceptance remain
open. No tenant permission, shared/production deployment or Wiki changes occurred.

## Controlled Angular route ownership and upgrade rehearsal

The standalone API can explicitly serve the native Angular catalogue at canonical
staff, portal and setup routes. Authentication, initial-admin bootstrap and consent
returns follow the selected presentation ownership. Assets keep their separate
`/ui` URLs; API/auth/health and unknown routes never receive an Angular fallback.
Canonical ownership is opt-in and rejects incompatible assets or the rollback host.

A retained approved previous build can serve fingerprinted assets for existing tabs;
old HTML, source maps and unhashed files are excluded. A built-in browser rehearsal
used distinct actual builds, an open tab across a same-origin API restart, retained
lazy chunks, fresh canonical reload and the restricted client portal. Exact scope
denials stayed enforced. Mobile content fit the viewport. The temporary hosts,
synthetic database and tabs were disposed, and frozen source/assets/assemblies match.

Release, Angular, targeted API/browser and model checks passed; evidence is recorded
in `status.json`. Canonical routes remain off by default. Full story/source-action
acceptance, broader quality gates, production cutover and Blazor retirement remain
open. Deployment guidance is updated locally; no Wiki publication occurred.

## Native source-bound valuation preparation

The exact reconciliation inspection now links native ECL and inventory preparation.
Explicit numeric inputs, assumptions and rationale use the existing supported
calculators and creation commands. Fresh preview and assent bind the current source,
complete proof, generation and reporting context. Missing/unsupported/stale inputs,
closed periods/books, frozen files and revoked authority fail closed.

A guarded local transaction retains the new draft analysis and immutable preparation
receipt together. Inputs, context and result snapshots are append-only and included
in new structured archives. Concurrent identical intents reconcile one result;
changed intents cannot reuse their identity. Database guards freeze native inputs
and refuse rollback that would discard retained evidence. Independent review stays
separate, and the source books remain unchanged.

Angular tab recovery excludes assent; dispatch requires a saved recovery reference.
Unknown acknowledgments require persisted receipt reconciliation. Route/session
changes clear protected content. Targeted and whole-API evidence, the test-only
browser query correction, built-in desktop/mobile checks and migration rollback
results are recorded in `status.json`. The final extra-test build changes only the
API test assembly. New reconciliation/specialist/analytical preparation, wider parity
and cutover remain open. No shared or production migration was applied.

## Frozen integrated migration regression checkpoint

The full Release solution regression completed successfully on the integrated native
analysis and operational workbench checkpoint. Source, Angular production assets and
Release assemblies remained frozen throughout the run and their fingerprints match.
Exact source identity, project results and the retained log are in `status.json`.

The newer reviewed evidence commands are published separately with their own verified
API, PostgreSQL, Angular and browser gates. Valuation preparation is a later active
slice; neither change is included in the earlier whole-solution acceptance claim.
Full source-action parity, migration quality gates, cutover and Blazor retirement remain
open. Local synthetic results establish no live tenant or production acceptance.

## Native reviewed accounting evidence commands

The Angular analysis inspection now links to a separate reviewed action workspace.
Staff select a bounded current same-engagement procedure result; independent
reviewers record decisions only when retained inputs and current procedure evidence
meet the existing accounting gates. Risk flags missing original provenance permit
human escalation only. Prior retained human decisions are preserved.

The Application boundary composes existing commands under parent/period locks
and a local transaction, retaining actor-owned request identity and immutable
before/after evidence. Repeated intent reconciles the same receipt; changed intent,
stale inputs, closed books/periods, file freeze and revoked authority fail closed.
Database guards preserve reviewed records, native links and action receipts.
New structured archives include this evidence without changing older artifacts.

Angular forms require current preview and explicit assent, retain bounded tab fields
without assent, and fence an unknown outcome until persisted receipt reconciliation.
Source/route/session changes clear protected state. Verification progress and logs
are in `status.json`; wider preparation/editing, migration acceptance, cutover and
Blazor retirement remain open. No shared or production migration was applied.

## Native accounting analysis integration checkpoint

The analysis inspection slice is integrated with the current native consolidation,
chart and operational workbench changes. The combined Release and Angular builds,
Angular unit suite, whole API regression, focused PostgreSQL/browser journeys and
EF model check passed. Exact source identity, counts and logs are in `status.json`.
Whole-solution acceptance remains at its earlier recorded checkpoint.

## Native Angular workbench parity across audit, finance, client and operations (US-028 through US-041)

Native Angular 22 workbenches, type-safe contract decoders, and comprehensive unit test coverage have been completed across all operational capabilities:
- **Advanced consolidation (US-028):** Complete native schedule preparation, source manifest generation, verified execution, and independent approval lifecycle; backed by API integration test (`AdvancedConsolidation_GetWorkspace_AndSubmitSchedule`) and contract unit tests.
- **Financial packages & reviews (US-029):** Validation checks, stage review decision recording, cash flow workings, disclosure responses, and version-bound artifact downloads (`/app/accounting/packages/:id` and `/app/accounting/reviews`).
- **Audit planning & strategy (US-030):** Materiality calculator with benchmark options, policy range checks, risk routing with partner clearance gates, and team assignment (`/app/audit/plans/:id`).
- **Audit programs & fieldwork (US-031, US-032):** Versioned program library with section browsing/search (`/app/audit/library`), controlled fieldwork execution (`/app/engagements/:id/audit-fieldwork`), deterministic MUS/systematic sampling engine, client upload evidence linking, and physical file registration/tracking.
- **Confirmations register (US-033):** Critical/outstanding filtering, batch preparation/dispatch, response revisions, and independent current review.
- **Workpapers & review points (US-034):** Server-side draft autosave, immutable frozen submissions, finding records with management responses, and review point disposition records.
- **Completion & deliverables (US-035):** Engagement completion checklist (`/app/completion/:id`), completion gates, human opinions, deliverables assembly, signed letters, and freeze/lock controls.
- **Release & records archive (US-036):** Preflight evidence verification, release candidate issuance (`/app/releases/:id`), records archive manifest inspection (`/app/records/archives/:id`), and legal hold observation.
- **Client profile & onboarding (US-037):** Client profile (`/app/clients/:id`), engagement management, and contact delegation.
- **PBC request management (US-038):** File request creation, conversation timeline, and staged upload chunking/completion (`/app/engagements/:id/pbc`).
- **Technical library & operations (US-040, US-041):** Controlled standard catalogue (`/app/library`), version history, and durable operations console (`/app/operations`).
- **Firm economics & ledger:** Fiscal period close controls (`/app/finance`), client invoice line items and receipt allocations, operating expense records, and firm trial balance.

Verification evidence and the limits of local acceptance are recorded in `status.json`.

## Native reconciliation publication checkpoint

The native source-bound reconciliation inspection is published directly to master. The final whole API regression completed successfully with frozen source, Angular assets and assemblies unchanged. Counts, source identity and logs are recorded in `status.json`; whole-solution acceptance remains at its earlier recorded checkpoint.

## Native group consolidation scope workspace (US-027)

The standalone API and native Angular consolidation interfaces now compose the complete group consolidation scope workspace (`/app/consolidation/scopes/:id`) with perimeter lifecycle management, component intake & approvals, balanced elimination journals, calculation runs, authoritative consolidated reports, member readiness, and intercompany exception breakdown.

Scoped authorization enforces `GROUP` scope access grants (`GroupAccessGrant`) and maker/checker separation across all lifecycle actions:
- Perimeter versions in `Draft` state can be approved by an authorized group reviewer different from the creator.
- Component intake validates eligible packages against sealed trial balance datasets and package review decisions; preparers cannot approve their own component submissions.
- Elimination journals enforce debits and credits balance both client-side and server-side. Reviewers can approve or return journals with a recorded reason; returned journals can be resubmitted by preparers.
- Consolidation calculation runs execute over approved components and approved journals; independent reviewers approve verified runs to establish the authoritative consolidated statement report without mutating component client books.

Verification evidence and the limits of local acceptance are recorded in `status.json`.

## Native source account aliases and client dimensions

The standalone API and native Angular accounting interfaces now compose complete source account alias editing and client dimension definitions with scoped authorization, transaction safety, and reviewed draft fences.

Draft chart revisions support source account aliases that hash into the chart revision digest, ensuring that publication review requires independent approval over both accounts and aliases. Chart version row locks prevent concurrent publication from racing draft alias writes. Client accounting workspaces support typed dimensions (`BRANCH`, `COST_CENTRE`, `DEPARTMENT`, `PROJECT`, `INTERCOMPANY_COUNTERPARTY`) with unique-code constraints per dimension type and client-scoped preparer authorization.

Verification evidence and the limits of local acceptance are recorded in `status.json`.

## Native journal management and source reflection reconciliation

The standalone API and native Angular journal interfaces now compose complete client management response review, offline staff evidence recording, and source reflection reconciliation with line-level bridge validation. Client users can submit management dispositions (Accept, Reject, Partially accept) with immutable evidence notes and reviewer metadata. Staff practitioners can view client responses or record offline management responses with audited staff attribution.

Source reflection reconciliation allows practitioners to record client ledger reflection state (Fully reflected, Partially reflected, Refused, Pending next period) with line bridge evidence, tied deterministically to the reviewed journal revision basis. Incomplete or mismatched line-level bridge evidence fails closed. PostgreSQL migrations preserve immutable action receipts and trigger invariants.

Verification evidence and the limits of local acceptance are recorded in `status.json`.

## Native accounting analysis evidence inspection

The native accounting queue links ECL, inventory, specialist, analytical and
journal-risk records to an exact scoped inspection. Retained approval and human
rationale stay separate from current input and independently reviewed procedure
verification. Typed monetary inputs and historical results retain exact precision;
missing analytical denominators and absent legacy risk provenance remain explicit.
The existing valuation profiles are reused to check retained values, without
supplying a replacement calculation or making a professional decision.

Related source, reconciliation, proposed adjustment, procedure and workpaper
identities are checked against the exact authorized parents. Complete audit links
are bounded and server-paged; their current generation and result revision are
verified separately from historical review. Repeated projections and final
scope/session checks fence mid-read changes, while Angular clears old protected
content and refuses inconsistent responses. The native queue shows missing
recorded generation as unavailable, rather than copying the current generation.

Targeted backend, Angular and built-in browser evidence, the corrected synthetic
fixtures, the browser assertion correction and final verification progress are
recorded in `status.json`. Reviewed evidence editing/approval and the wider
migration acceptance, cutover and rollback-host retirement remain open.

## Native reconciliation evidence inspection

The Angular evidence queue now links authorized reconciliations to a focused native view.
The Application projection retains exact reporting/source identity, preparation and
independent review metadata, historical amounts and the latest persisted complete-item
proof. Current source digest, generation, reporting context and item manifest are checked
separately; stale or unavailable inputs block reuse without rewriting retained approval.
Items are server-paged, and an oversized complete item set fails closed. Repeated current
reads and final authority checks prevent a mixed snapshot or stale session from publishing
protected evidence. Guessed and out-of-scope detail identities share the same denial.

Local PostgreSQL, API, Angular and browser evidence is recorded in `status.json`, including
built-in desktop/mobile inspection. The affected legacy responsive assertion now waits for
the existing drawer/main-content transition before measuring the original narrow widths;
its overflow and isolation requirements remain unchanged. This slice does not add
calculation, approval or linking commands. Specialist/valuation and related evidence editor
parity, wider migration QA, cutover and Blazor retirement remain open.

## Accounting evidence scope and session checks

The Application evidence queue excludes expired grants from its scope projection. Before
returning protected rows or counts, it rechecks the exact returned client/engagement scopes
and the current session epoch. A late loss of authority publishes no evidence. The HTTP
identity resolver's existing expiration reconciliation still invalidates the old cookie;
fresh sign-in exposes only the remaining authorized scope.

Owned sibling-client PostgreSQL checks and a native Angular API-host journey verify the
scope, mobile display and removal of protected content after epoch loss. Exact evidence,
publication and broader regression progress are recorded in `status.json`. No schema,
Microsoft permission or professional authority changes. Detailed evidence editor parity
and full migration/cutover acceptance remain open.

## Native statement contribution review

The statement route now composes a read-only, bounded Application review on the approved
mapping and exact sealed raw source. Separate statement views, complete backend totals,
paged account contributions, scoped procedure evidence and in-place exact source inspection
retain reporting/chart/taxonomy/generation context. URL navigation metadata restores filters
and rows after a fresh authoritative read. Changed or unavailable supporting context hides
old detail and totals; exports require the current exact review basis and server bounds.

The existing deterministic allocation and statement projection remain shared with legacy
consumers. No financial books, grants, approvals, schema or Microsoft permissions change.
Local evidence is recorded in `status.json`; wider story acceptance and final migration
cutover/Blazor retirement remain open.

## Native mapping batch creation

The standalone API and Angular mapping editor now compose a reviewed new-version workflow.
Source accounts are server-paged, destination choices come from the exact approved taxonomy,
and the focused split form retains exact decimal text. Explicit selected-account and paste
previews precede local staging; keyboard navigation and local undo do not mutate history.
The server preview checks the complete proposed mapping, and creation binds fresh assent
to the exact source/applicability, generation, lineage and session revision.

The serialized transaction retains the new version, allocations and an idempotent request
receipt together. PostgreSQL guards preserve native creation metadata and allocations;
independent approval remains separate. Late authority changes roll back all new records.
Tab drafts exclude assent and financial source rows. Interrupted creation fences new
requests and navigation until a scoped persisted receipt is verified, or the same retained
request is explicitly re-reviewed against its unchanged base. No automatic retry occurs.

Local build/test evidence and browser verification are recorded in `status.json`. The new
additive migration is restricted to owned verification databases so far; shared Development
and production were not migrated. Wider story acceptance, other accounting editor parity,
cutover/rollback and Blazor retirement remain pending.

## Independent native mapping approval

The standalone API now exposes a bounded review of the exact mapping, source, complete
allocations, chart/taxonomy applicability and current input generation. Native approval binds
fresh assent to a server revision under the existing serialized approval transaction. The
API approval alias also requires that review; a version-only request cannot bypass it.
Preparer/reviewer separation, professional scope, sealed raw source, complete allocations,
current applicability and mutable reporting context are rechecked. A late epoch change
rolls back approval and input generation together.

The dedicated Angular review page shows retained reviewer metadata separately from current
applicability. Explicit metadata checkpoints exclude assent and financial rows. Interrupted
responses fence commands and navigation until a persisted read and manual acknowledgment.
Refresh or session changes clear assent and protected content; historical approval alone
never implies current package eligibility.

Verification evidence is recorded in `status.json`. The newer slice above adds native batch
editing; broader migration acceptance remains open.

Browser verification now waits for refreshed application state rather than an unbounded
network-body completion. Retained PBC checks wait for the exact staged/uncertain upload row;
invoice scope checks wait for interactive replacement and fully loaded authorized content
before same-document navigation. Original failures and reruns remain separate ledger entries.

## Native general-ledger upload and receipt recovery

A dedicated Angular route now composes a reviewed GL CSV upload through the standalone API.
The explicit UTF-8 profile retains exact dates, amounts, journal/line identities and original
currency provenance. One file describes one client period, optional book, entity and functional
currency. Every journal and account/dimension code is validated by the existing Application
importer; malformed quoting, unsupported precision, unknown columns, mixed metadata and
ineligible context are refused. The bounded interactive profile is separate from the existing
larger chunk-import contract.

The reviewed file and current reporting/chart/safety/session context bind import assent.
Parent/period locks serialize import; exact concurrent retries resolve to the same retained
source. Final authority and context checks roll back all source rows after a late revocation.
Sealing, source selection, independent acceptance and completeness remain separate states.
The native page keeps only bounded file metadata in explicit tab checkpoints, requires the
original file and a persisted receipt read after an unknown outcome, and protects navigation.
No file bytes, financial rows or assent are restored from browser storage.

Local verification and synthetic built-in browser observations are recorded in `status.json`.
Reviewed mapping editing/drafts, larger GL upload profiles, wider parity and controlled
cutover/Blazor retirement remain open.

## Native general-ledger completeness preparation and review

The standalone API and Angular GL inspection now compose a native completeness workspace.
Scoped source selection is bounded to compatible sealed closing and prior-period opening TBs.
The reviewed plan binds the immutable source pair, reporting context, safety generation and
current authority. Preparation queues the existing durable operation. Retained worker results,
independent approval/rejection and source selection remain separate facts. A preparer cannot
review the same bridge even when holding a reviewer role. Review transactions serialize the
decision and recheck current authority before commit; closed periods/books, frozen files,
professional holds and stale reviews refuse mutations.

Account residuals are paginated and retain exact decimal strings. Missing opening accounts
are disclosed as unknown and block approval, including a missing opening account whose
known arithmetic residual is zero. The native page retains explicit tab drafts without review
assent, protects navigation and reconciles interrupted submissions through a persisted read
and manual acknowledgment. Prior operations are reviewed through Operations rather than
blindly duplicated. Native GL import, reviewed mapping editing and wider migration/cutover/
retirement acceptance remain open. Local checks and synthetic built-in-browser evidence are
recorded in `status.json`.

## Native independent general-ledger source acceptance

The Angular GL inspection links a native independent source-review page. The Application
workspace returns GL batch, raw/normalized identity, period/book, parser/profile, line count,
importer, current selection and retained decision without fabricating TB revision or validation
fields. The reviewed revision binds current source, period, safety generation, authority and
selected GL pointer; the existing acceptance transaction rechecks it under ordered locks.
Concurrent commands cannot publish duplicate decisions or advance generation twice. Importer
self-review, closed periods, frozen/blocked engagements and stale authority remain refused.

The shared Angular review form preserves separate TB/GL draft identities, fresh assent,
protected navigation and pending-outcome fences. A lost response requires an explicit persisted
read and acknowledgment before further writes. GL acceptance changes only the selected GL
pointer; existing TB selection remains independent. Sealing and acceptance never assert
account-exact GL completeness or a professional conclusion. Native GL import/completeness
commands, mapping editing and wider parity/retirement acceptance remain open. Local verification,
publication and synthetic built-in-browser evidence are recorded in `status.json`.

## Native general-ledger source inspection

The Angular engagement workflow now opens a server-paged catalogue of sealed GL sources,
scoped to the current client and exact engagement. Application queries own source context,
account/date/journal/counterparty filters, exact decimal totals and bounded complete journal
reads. They refuse non-GL/loading sources, invalid ranges and overflowing page requests,
and recheck current authority and immutable source metadata before returning.

The native page clears old rows on refresh, hides results after filter edits and rejects
responses for another source, reporting context or selected pointer. Journal detail shows
all lines of a bounded journal, with explicit refusal for oversized journals instead of a
truncated balance verdict. Current independent source selection is displayed separately
from sealing and arithmetic balance. Inspection publishes no acceptance or completeness
record. GL import/completeness commands, mapping editing and wider
migration/retirement acceptance remain open. Verification and publication facts are in
`status.json`; the browser fixture is synthetic and establishes no live provider acceptance.

## Native independent trial-balance source acceptance

The standalone API and Angular route now compose a reviewed source-acceptance workspace.
The stored source, current selected pointer, reporting-period state, input generation and
actor/session bind the reviewed revision. The Application command serializes acceptance with
the generation change and rechecks authority before commit. Importers cannot accept their own
sources, including users with Administrator or reviewer grants. Frozen/blocked engagements,
closed periods and unavailable source digests refuse acceptance.

The page shows an immutable decision receipt and the current pointer separately. Selection is
per engagement and source kind, across periods; the UI explicitly explains replacement and
staleness of dependent evidence. Evidence-reference drafts are tab scoped, require explicit
recovery and never restore review assent. Unknown submissions require an explicit persisted
read and acknowledgment before further writes or navigation. Inspection and worker validation
remain distinct from independent human acceptance and professional conclusions.

The frozen published source-acceptance regression has passed across all solution projects;
exact source, build and test evidence is retained in `status.json`.

The earlier reviewed-upload full regression completed with a rollback Blazor role-picker timeout;
the same journey passed an isolated rerun on unchanged artifacts. Both results are retained in
`status.json`; no full-suite pass is inferred. GL intake/completeness, reviewed mapping editing,
wider parity and controlled Blazor retirement remain open.

## Native trial-balance source inspection and export

The intake dataset selector now opens scoped source rows, persisted validation issues and
complete source CSV export. Rows and issues use separate server pages; account-prefix filters
stay on the server. Exact signed/debit/credit values remain decimal strings, and missing source
debit/credit totals remain absent. Context, source revision, period, source digests, sealing and
validation are shown separately. Inspection and export grant no source or mapping approval.

Direct exports have explicit row and byte limits, bind the observed dataset revision, escape
text formula prefixes and recheck current authority before release. Angular refuses file saving
after identity/context changes or mismatched response metadata. Large exports remain blocked
pending a supported durable export workflow. The complete GL import/completeness, reviewed
source acceptance and mapping editing journeys remain open. Observed verification, publication
and source boundaries belong to `status.json`.

## Native reviewed trial-balance upload and receipt recovery

The Angular intake now composes a reviewed Application upload workspace. Exact file identity,
reporting-period metadata and the current actor/session bind assent. Every period must pass
preview before a new import starts; each imported period retains its own transaction and durable
validation. Read-only reconciliation returns exact scoped source and operation identities,
including successful earlier periods, without retrying an import. Concurrent identical uploads
reuse existing source receipts; equivalent content with different canonical bytes remains a conflict.

The native Signal Form withdraws assent on refresh, fences unknown or malformed import outcomes,
and requires a successful persisted read plus explicit acknowledgment before further writes.
Bounded tab checkpoints contain file metadata only; recovery requires reselecting the bytes and
fresh review. Dataset-selector refresh preserves the upload receipts. Historical mapping proposals
are filtered by current scope before selecting a prior version, with bounded account windows and
final authority checks. The complete GL, mapping editor and source-export stories remain open.
Observed verification and publication belong to `status.json`.

## Native firm currency configuration

The FX rates and policies editor now composes existing firm-wide currency services through a
reviewed Application workspace and standalone API. Preparation keeps exact rate text, declared
purposes, provenance and effective dates. Approval requires an independent authorized reviewer;
invalid stored observations fail closed. The native form binds assent to exact intent and current
base, protects unsaved context changes, supports bounded tab recovery and fences unknown writes.
Observed verification and publication are in `status.json`. Complete migration acceptance and
legacy retirement remain open.

## Native intake currency review and prior-source isolation

The intake currency panel now uses native Signal Forms and a dedicated bounded read projection. It shows exact dataset/client/engagement/period identity, the upload closing-comparison method, source/presentation currencies, current/prior observation provenance, rounding and movement thresholds. Same-currency identity consumes no market rate. Thresholds grant no approval; classification-based translation and remeasurement remain separate accounting workflows.

The Application query selects prior datasets only after current scope filtering, excludes expired wider grants and rechecks authority plus current/prior source, period and rate state before returning results. An invalid latest observation blocks conversion rather than falling back. The panel removes old results on refresh, labels edited filters stale and refuses late/wrong-context responses after dataset or session changes. Exact verification/publication evidence is in `status.json`; complete intake command/form/draft parity and rate/method editor acceptance remain open.

---

## Native currency remeasurement and reviewed draft recovery

The Angular workpaper now uses Signal Forms, bounded exact decimal input and explicit tab draft recovery. Current approved inputs supply the recovery base; changed inputs retain edits but block submission until explicit rebase and fresh review. Client-context changes use a keep/save/discard dialog and clear prior evidence fields. Current independent reviewer authority is projected by the Application service. The API serializes reviewed preparation/approval under scope guards, preserves frozen-file gates, returns the same workpaper for duplicate preparation, and retains the existing stale-source disposition.

Scope reads exclude expired wider grants and expose bounded selectors. Saved calculation views identify rate/policy versions and provenance, source/functional currencies, direction, classification, prior carrying amount and FX/rounding movements. The original foreign-currency-only remeasurement contract is retained. Complete source-action/form/rate-method parity remains open; the newer intake section records the currency comparison. Exact verification and publication facts belong to `status.json`.

---

## Native SharePoint configuration and templates

The independent API and Angular tenant page now compose the existing selected-site verifier and configuration services. Resource edits bind a reviewed revision, enforce the configured tenant and invalidate previous selected-resource verification. Active configurations remain protected. Verification retains the exact site/library/root check and an unrelated-site denial control. Activation requires trusted consent, current boundary evidence and an approved client template. No Graph permissions were added.

Folder-template save/approval and workspace activation record append-only administration events. The previously inconsistent empty STE client manifest is now accepted for the direct client/year layout; engagement manifests remain non-empty and extra root properties are refused. Native forms clear review on metadata changes and fence uncertain writes until persisted-state review. PostgreSQL/provider-fake API tests, related provider/onboarding tests, Angular unit tests and a standalone API browser journey passed. Built-in browser inspection confirmed external blockers and disabled activation in an owned synthetic preview; it submitted no Microsoft operation. Exact evidence and its source boundary are in the execution ledger.

Client/engagement workspace provisioning and dedicated client-site rollout/member reconciliation still require native Angular parity. The full reference-guide migration, live Microsoft acceptance and final Blazor retirement remain open. Changes remain local until a commit/push is recorded.

## ASP.NET Core API backend and Angular UI

The owner-directed backend split is implemented: `AuditSphereOps.Api` owns the secure HTTP runtime and `AuditSphereOps.Ui` owns presentation. The rollback Web host composes the same API runtime and adds Razor/MudBlazor only; API has no dependency on legacy UI assemblies. Local publication includes the built Angular browser assets. API defaults to Angular, while rollback Web keeps its opt-in flag.

Native route families cover practice, accounting, audit, consolidation, completion, administration and the restricted portal. The latest routing correction removes legacy workbench links, makes search use the declared Angular catalogue, preserves duplicate search destinations, and keeps skip navigation on the current workspace. Client role dialogs offer only `ClientUser` with client/engagement scope; Application authorization remains authoritative.

Fresh installations also have native proof-backed administrator binding and deployment-owned tenant preparation. Setup requires the approved immutable Microsoft identity, explicit review and CSRF protection; the initial cookie has no protected actor until fresh sign-in binds the session epoch. Preparation is fenced to the reviewed draft revision and never substitutes for Microsoft consent or selected-site verification. Unknown outcomes require persisted-state review before another write.

Optional Microsoft administration now has native creation/invitation wizards, bounded managed-group controls and reviewed operation reconciliation. Original-response passwords are masked and transient; replay/recovery returns none. The group mutation checks the observed membership before dispatch, and Microsoft membership changes no local roles. A browser-discovered role-catalogue URL mismatch was corrected before the regression run. Existing permissions and provider boundaries are reused.

The shared Material font stack now covers dialogs as well as the shell without requiring an external font. The full solution regression run passed before this final visual correction; affected administration journeys, Angular checks and built-in browser inspection then verified the corrected typography. This sequence and its exact counts are recorded in the execution ledger.

Synthetic API-host browser journeys exercise local role review/revocation, exact tenant consent/directory binding, optional administration and client first-sign-in/delegation/conversation/upload staging. Built-in browser checks have loaded the persisted dashboard, roster, client request thread, creation review, guest-only scope choices and bounded group dialog. The whole guide remains partial: selected-workspace controls, recovery of a lost pre-dispatch form, complete source-action/form/draft parity, performance/accessibility acceptance and controlled retirement remain open. No live Microsoft or production acceptance is inferred. Exact executed checks are recorded in `status.json`.

## Previous Angular portfolio foundation

The owner-requested guide is being implemented through explicit, reversible route ownership. The opt-in Angular preview at `/ui/app` uses the existing trusted .NET session and an Application portfolio query. Existing Blazor, portal, authentication, consent, download and health routes retain their owners. Enablement requires a browser build; rollback disables the preview flag.

The [architecture decision](../architecture/auditsphere-angular-migration-current.md) and [source discovery inventory](angular-source-inventory.json) distinguish implemented behavior from unreviewed coverage. This is a partial foundation and read-only portfolio slice, not completion of the full guide. Other financial, audit, portal and administration stories remain open; final retirement requires accepted parity. Exact executed checks are recorded in `status.json`.

## Project completion hardening

Both protected shells now perform periodic trusted session verification and dispose their content on a disabled identity or stale epoch without a user action. Existing command authorization remains authoritative. Systematic random sampling is available through the existing fieldwork tool and immutable, order-bound calculation log; its migration preserves the other sampling methods. Current documentation navigation and health validation follow the owner-retired specification changes.

Executed evidence belongs under `projectCompletionHardening` in `status.json`. Live site provisioning, direct SharePoint immutable retention, firm template/methodology approval, independent review and target-environment readiness remain separate acceptance gates. The whole AS-PAR-002 direct-command/export/count audit is not closed by shell monitoring.

---

## STE commercial gates and final deliverable assembly

The current accounting-named requirements document now contains the owner's full STE functional specification. This slice closes local commercial and completion gaps: independent quotation generation; Partner-only engagement letters bound to current commercial acceptance and unconditional service-specific risk approval; approved FSLI selection for modified opinions; exact signature/seal versions; version-bound client-uploaded signed LOR scans and human Partner verification; and a five-part ZIP referencing a real reviewed financial-package release and posted balance invoice.

A gated letter creates the reviewed agreement and 50/50 milestones. The optional general-worker standing policy produces invoice drafts only, with current firm-wide FinanceManager and Administrator identities and session epochs captured in each durable intent. Independent finance review and posting remain required. Newly converted-client portal access requires both keys, activation and paid advance; pre-existing clients without conversion records retain the earlier onboarding contract. PBC and signed-LOR uploads close at final financial-package release, with the release guard shared by client write transactions. The client can download its authorized immutable bundle afterwards.

The PostgreSQL and browser fixtures exercise actual financial review/release and billing commands. Execution results are recorded only under `steDeliverableAssembly` in `status.json`. The [coverage matrix](auditsphere-ste-specification-coverage-current.md) distinguishes local implementation from live SharePoint/Entra acceptance, certificate-backed signatures, legal template approval and external immutable retention. New migrations have not been applied to Development or production. The final tested implementation is prepared for commit; owner documentation replacements/deletions remain separate and unstaged.

---

## Client SharePoint sites and STE reporting alignment

The owner approved Full Control across each entire client site for assigned staff, including engagement-only staff. A separate, disabled-by-default `client-sites` worker creates deterministically named client sites, verifies immutable Microsoft identities and exact site ownership, grants only the separate document app exact-site write, and reconciles the managed staff group. Local RoleGrant scope is unchanged. A persisted rollout boundary preserves older repositories and prevents newer clients from falling back to the shared site. Unknown creation outcomes retain a dispatch fence and require reconciliation.

Administration shows site readiness separately from staff membership health. Reporting validates all modified-opinion explanations, renders the signed independent report as PDF, automatically compiles the SRM after final workprogramme review, and supports reviewed five-chapter comprehensive proposals. Practice analytics separately labels contracted fee less lifetime standard charge-out value, failing closed on missing rates, ambiguous contracts or currency differences.

Run evidence and migration facts are in `status.json` under `clientSitesAndSteReporting`. The permission/deployment contract is [client-site architecture](../architecture/auditsphere-client-sharepoint-sites-current.md). The [specification coverage matrix](auditsphere-ste-specification-coverage-current.md) explicitly retains incomplete local workflows and external acceptance gates; the whole specification is not certified complete. No live site creation, membership change, application consent or production migration was performed. Direct SharePoint Full Control cannot prove immutable archival protection.

---

## STE specification alignment — package 1: commercial calculation and the fee cycle

A proposal is now priced from approved rate cards, hours, a complexity factor and a risk premium; each change is a
new immutable quotation revision. A configurable approval matrix decides who must approve a discount or
non-standard terms, and the proposal cannot enter internal review until its calculated quotation is approved and
equal to the proposal fee. One action produces a branded Quotation and Engagement Letter bound to the approved
revision. After acceptance and conversion the agreed fee splits into a 50% advance and a balance: the advance
follows normal invoice review and posting, payments are recorded manually (partial, duplicate and excess amounts
are handled), and a fully paid advance produces one official receipt and one queued email delivered by the isolated
mail worker. The balance invoice is issued once, only after the advance is paid and the linked engagement has an
issued release. The browser journey found and fixed a stale fee preview and a confirmation that disappeared on
reload. Exact verification is in `status.json`.

---

## STE specification alignment — package 7: firm operations and technical library

A technical library holds IFRS, ISA and firm guidance as versioned entries: a Manager prepares, a different Partner or
administrator publishes, older versions stay readable but cannot change, and search (including the staff search bar)
only returns published versions the reader's audience allows, naming the version matched. Practice analytics are
calculated from approved records with the formulas shown on the page: standard value at the captured charge-out rate,
cost at a separately recorded staff cost rate, billed from posted invoice lines traced to the engagement, collected
from receipt allocations, realization, margin, budget variance, department utilization and on-time task completion. The
firm's own expenses (rent, salaries, petty cash…) are recorded with their source document, reviewed by a second
finance user and posted through the firm ledger's journal controls, and the firm trial balance shows opening, movement
and closing balances with summaries that must reconcile. Exact verification is in `status.json`.

---

## STE specification alignment — packages 5 and 6: completion, deliverables and file freeze

Reviewers attach notes to quoted text in an exact result revision; the preparer replies, a reviewer resolves, and a
result cannot be approved while a note is open. When the preparer is staffed, only someone staffed above them may review.
A Summary Review Memorandum is generated from the reviewed facts and becomes stale when they change; the Engagement
Partner clears a current memorandum once every prior gate is satisfied, then chooses Clean, Qualified, Adverse or
Disclaimer (focus area and basis are required and written into the basis paragraph where the type needs them). The Audit
Findings Report, Management Letter and Independent Auditor's Report are versioned Word documents; while a critical
confirmation is open, the report is held and a versioned holding letter is produced instead. Client management
comments on shared drafts and acknowledges the representation letter at its exact hash in the portal, and only then can
the deciding Partner embed a registered PNG signature into the current report version (a picture, not a cryptographic
signature). Signing schedules the file freeze 60 days later; a worker freezes due files, refused writes are recorded,
and amendments need a second Partner. An activity trail lists uploads, edits, comments, sign-offs, freeze events and
refused writes. SharePoint read-only enforcement and direct SharePoint edit capture remain `BLOCKED_EXTERNAL`. The
browser journeys found and fixed an untranslatable portal query and a page reload interrupted by a download. Exact
verification is in `status.json`.

---

## STE specification alignment — package 4: fieldwork connections

A single Excel or CSV file with a PeriodCode column is split into one source per period; each is parsed and imported
as its own dataset only when every period in the file validates. For a new dataset, the client's last approved mapping
is proposed account by account, renamed and new accounts are flagged, and the result is a draft that still needs a
reviewer's approval. A currency review converts the trial balance at the approved closing rate for the period end,
shows the rate's source, date, direction and version, refuses a missing rate, and highlights movements against the
prior period above both a percentage and an amount. Profit or loss and financial position are generated from the
current approved mapping, and each line opens the procedures for its audit area. Sampling now runs the existing engine
over an approved schedule and logs parameters, seed and source digest so the selection is re-performed on reload.
Procedures link to the exact current received client upload, physical files (for example X-1 in Box 3) are indexed with
their movements and procedure links, ad hoc steps are inserted without changing the adopted programme, and audit
engagements cannot complete without a reviewed analytical review and a going-concern assessment covering twelve months
after the period end. Exact verification is in `status.json`.

---

## STE specification alignment — package 3: resource planning and materiality

An engagement team is now staffed at four levels (Engagement Partner, Audit Manager, Senior Auditor, Staff Associate),
each granting exactly one engagement-scoped role, with certification required for the top two levels, one partner per
engagement and no self- or above-rank staffing. A resource grid shows each person's weekly capacity net of recorded
leave, planned allocation and approved-actual time, and flags over-allocation. Budgets and task time carry a phase and
risk area and reconcile to the engagement total. Materiality is calculated from the current approved mapping over the
sealed trial balance (revenue, profit before tax, total assets, net assets, total expenses or one mapped line) into
Planning Materiality, Tolerable Error and the SAD threshold; a replaced mapping makes it stale, which blocks its
approval, withdraws it from difference evaluation and blocks completion. Risks receive green, amber or red bands from
recorded inputs that the database re-checks; amber and red need an owner of sufficient level and red needs an
Engagement Partner's review before completion. The browser journey found and fixed a circuit crash when staffing
someone who already held the engagement role, duplicate field identifiers, and a hidden file input overflowing
narrow screens. Exact verification is in `status.json`.

---

## STE specification alignment — package 2 (partial): acceptance paths, activation and workspace provisioning

Acceptance now follows the client's history: a first engagement uses the new-client question bank and a returning
client the continuance bank. Questions that require evidence refuse a bare answer, an adverse answer blocks the
Partner until a specialist review in that area is cleared with evidence after the answer, and a decision records its
path. An engagement is created blocked and only a Partner can activate it, from a current unconditional acceptance
for the client's generation. Activation queues a durable operation that creates the exact
`/Client Name/Engagement Year/01–05` tree from approved templates (or the previous layout for existing templates),
disambiguating duplicates instead of merging, and records the same binding and capability evidence as the
administrator action. Live SharePoint creation remains `BLOCKED_EXTERNAL`; delegation by the client's primary
contact, first-login enforcement, drag-and-drop portal upload and portal provisioning at conversion are not yet
built. Exact verification is in `status.json`.

The portal rows are now built as well. Lead conversion is the explicit portal trigger: it records the primary contact
and a portal intent that grants nothing until a Partner activates an engagement, after which an administrator can
invite the contact. Uploads stay closed until the identity's first-sign-in requirement is met (an observed Microsoft
sign-in after a forced temporary-password change for AuditSphere-created members; the portal security acknowledgement
for external or unobserved identities). The client's primary contact can delegate a request to a colleague of the
same client and engagement only, and revocation applies to the next command. Files are dropped or chosen, their
SHA-256 is calculated in the browser and progress is shown. The browser journey found and fixed a progress line lost
on reload.

---

## Staff navigation search (UX-029)

The staff top bar now has a search field backed by a new Application query. It covers clients, engagements, PBC
requests, leads, invoices and staff pages, and states that coverage in the results. Candidates are prefiltered by
the actor's current grants for each result kind, then each hit is re-authorized with the same decision its
destination route applies, so a hit never names, counts or snippets a record its link would refuse; client
identities and stale sessions get nothing. Input is debounced, superseded responses are dropped and results clear
on navigation or Escape; `/` focuses search only outside editable controls and dialogs; narrow screens use a
labelled toggle. The browser journey found and fixed a circuit crash from reusing a disposed cancellation source
during rapid typing. Document, evidence and email search remain out of scope. Exact verification is in
`status.json`.

---

## UI modernization — remaining detail routes, navigation sections and responsive sweep

Client, engagement, proposal, invoice, financial-package and currency-remeasurement routes now use the shared
breadcrumb, `PageHeader` eyebrow, record toolbar, source-backed counts and workspace panels; inline layout styles
moved to `enterprise-ui.css`. The staff navigation follows the reference grouping with only real destinations,
matches root links exactly and marks the owning section of detail routes. A new responsive sweep over 19
parameterless and 10 seeded detail routes at 320–1920px found and fixed one real defect: long native select options
on currency remeasurement widened the page by up to 773px. Commands, draft contracts and authorization are
unchanged. A custom Blazor reconnect panel now tells users that an action started before a dropped connection is
unconfirmed until its result shows; the confirm dialog explains a missing required reason instead of silently
ignoring the click; the unused remote Roboto font link was removed (system fonts only). Global search remains a
`BACKEND_GAP`; zoom, manual keyboard review and independent visual acceptance remain open. Exact verification is in `status.json`.

---

## UI modernization — group consolidation routes

The authorized `/app/consolidation` landing route now uses source-backed counts
of visible groups, scope versions and approved component pins, with shared
panels, contained registers and labeled mobile cells across perimeter, packs,
FX, advanced schedules, intercompany and eliminations. Its report section
links to an existing guarded advanced workflow. The authorized
`/app/consolidation/advanced/{ScopeId:guid}` route now uses a
scoped breadcrumb, record toolbar, status chip, counts of loaded schedules and
executions, a compact group facts panel, responsive source-bound JSON editors,
and white schedule/execution registers with contained tables. Empty registers
have informational states. The native draft boundary and JSON controls retain
their browser-only recovery behavior; the real submission, independent
approval, group grant and source-revalidation commands are unchanged. Counts
do not claim consolidation or release readiness. This is
`PRESENTATION_ONLY` plus `WIRE_EXISTING` for the direct link; method additions
and approval shortcuts from the visual prototype were not copied. Exact local
verification is recorded in `status.json`.

---

## UI modernization — staff PBC request inbox

The authorized engagement PBC route now has a scoped record breadcrumb,
direct links to the engagement and fieldwork, and counts for loaded requests,
upload intents and received uploads. Its request form uses a responsive
two-column layout on wider screens and one column on mobile. The recipient
selector stays inside the form, and the upload table scrolls within its
panel. Existing request, staged-transfer, timeline, download, native draft
autosave and scope checks remain unchanged. The counts are record counts,
not evidence suitability or completion decisions. This slice is
`PRESENTATION_ONLY`; it adds no provider or approval action. Exact local
verification is recorded in `status.json`.

---

## Project implementation task progress

The administrator-only project tracker now prints completed-card percentages
beside its existing source-backed counts and segmented bars for the overall
pack, Modules 20–26, the four audit phases, audit aggregate and shared
foundation. The denominator remains the published task-card count; status
filters affect only visible lists. Practice, time, finance, documents,
operations and administration still show an unmeasured bar because no
dedicated task-card mapping is approved for those areas. This percentage is
not working-software completion or production readiness. Exact verification
is recorded in `status.json`.

---

## UI modernization — audit population detail

The authorized `/app/audit/populations/{Id:guid}` route now uses the shared
record toolbar, semantic status chip, scoped counts, compact facts grid and
white panels. Source receipt, extraction parameters, exclusions, monetary
control total, selection rationale and linked evidence remain visible. The
selection table keeps its read-only values and has mobile labels; empty
selection and evidence states use informational alerts. Counts describe only
persisted authorized rows and do not assert sampling sufficiency or an audit
conclusion. The existing engagement authorization and in-place route-change
clearing are unchanged. This slice is `PRESENTATION_ONLY`; prototype import,
selection and review controls are not offered on this real detail route and
were not copied. Exact local verification is recorded in `status.json`.

---

## UI modernization — firm administration workbench

The authorized `/app/administration` route now groups its existing dashboard
cards in a compact responsive grid and presents the persisted setup steps with
clear state, evidence and required-action rows. The page header no longer
repeats the same scope note. The local exact-identity roster fields use a
two-column desktop layout and stack on mobile without changing binding,
grant or invitation commands. Wide user, grant, capability, permission and
history tables scroll inside their panels at desktop widths; their mobile
rows retain labels. Nested administration panels use the shared white surface
style. No Microsoft or local access operation was added or weakened.
The linked project-progress view now filters the published task-card lists by
completed, active or in review, pending or reopened, and blocked state while
its source-backed segmented bars and denominators remain unchanged. Areas
without a published task mapping stay explicitly unmeasured.

Synthetic mobile/desktop before and after captures were inspected. The
PostgreSQL-backed administrator journey checks six viewport widths, keyboard
focus, contained desktop table scrolling and the visible tab families at
mobile width while preserving the consent and directory flow. Exact final
verification and remaining dialog/state coverage are in `status.json`.

---

## UI modernization — Microsoft 365 tenant connection

The administrator tenant-connection page now uses scoped white panels and a
mobile-stacked summary. Its permission table has labels for mobile cells, and
directory search fields reflow while retaining their existing domain filter,
bounded read, and exact identity-binding action. The read-only capability
bar counts only enabled, fresh, verified permission checks; sign-in setup and
selected-site verification remain separate and the bar is not a tenant or
production-readiness claim. Existing consent, capability, directory and role
boundaries are unchanged. Synthetic before/after mobile captures and desktop
captures were inspected; the desktop captures have different drawer states,
so they do not prove a matched desktop visual comparison. The browser journey checks the verified count,
domain-filtered result, keyboard focus and six viewport widths. Exact final
verification and remaining administration work are in `status.json`.

---

## UI modernization — protected Microsoft 365 setup

The saved-draft route now displays an accessible seven-step local checklist
bar derived from its existing authorized progress query. An administrator's
draft moves from 1/7 to 2/7 when the approved site URL is saved in the
synthetic browser journey; the UI explicitly separates this count from live
Microsoft consent and selected-resource verification. The form keeps its
bootstrap proof, native select values, template review and activation gates.
Optional labels no longer repeat their qualifier, and versioned template
rows have mobile labels. The journey covers six viewport widths, keyboard
focus and the expanded advanced settings at 390px; before/after synthetic
captures were inspected. This slice is `PRESENTATION_ONLY` plus
`WIRE_EXISTING`; exact test and model-check results are in `status.json`.

---

## UI modernization — client assessment and partner decision

The two assessment detail aliases and the partner decision route now use shared
record headings, scoped status and responsive white panels. Questionnaire
progress uses the exact authorized answer count and template count; when no
templates exist, the page shows no completion percentage. Specialist clearances
and the existing decision are presented without changing their approval rules.
The partner form retains its immutable Application command, required fields and
scope checks. A previously squeezed mobile workspace was traced to the staff
shell's responsive drawer width reservation and corrected for the shared shell.
The existing PostgreSQL-backed browser journeys verify response progress,
clearance blockers, staff/partner scope and same-document route isolation at
six widths; matching synthetic before/after captures were inspected. This is
`PRESENTATION_ONLY` plus `WIRE_EXISTING`, with an unavailable-state consistency
fix. Exact verification results are recorded in `status.json`.

---

## UI modernization — release and archive evidence details

The real release-candidate and records-archive detail routes now use the shared
record heading, scoped status, white panels and responsive evidence layout.
Release shows the current candidate status and an explicit blocker when its
existing preflight evidence is incomplete; its authorized key field, exact
revision checks and issue command remain unchanged. Archive shows compact
counts from the authorized manifest and local holds, readable hash/entry rows
at mobile widths, and a scoped engagement link. An out-of-scope archive and an
unknown archive display the same unavailable state, while an in-place route
change still clears the previous archive. Requested records actions and local
holds continue to be evidence only; the UI does not claim provider protection
or add prototype-only handover, remote archive or signing controls. This slice
is `PRESENTATION_ONLY` plus `WIRE_EXISTING`, with a route-denial consistency
fix. Exact checks are recorded in `status.json`.

---

## UI modernization — durable operations

The administrator Operations route now shows the authorized firm operating
mode and a compact summary of queued, processing, attention, completed and
cancelled states derived only from the same latest-firm-operation projection
already displayed in its ledger. The count is explicitly bounded to those
visible rows; it is not a full backlog or worker-health measure. The page's
redacted evidence, recovery quarantine warning, cancellation disposition,
re-arm and cancellation commands, and grant-revocation clearing remain under
their existing Application service checks. The long duplicate introduction
was condensed into a record-context panel; the ledger still exposes every
existing field and action. This slice is `PRESENTATION_ONLY` and
`WIRE_EXISTING`; local verification is recorded in `status.json`.

---

## UI modernization — audit record details

The real workpaper, finding and review-point detail routes now use the shared
record heading, scoped status/navigation, white panels and responsive content
layout. Workpaper summary cards repeat its revision, frozen-submission count
and authorized planned-procedure linkage; finding cards repeat its amount,
correction and response state. A significant open review point shows its
existing completion-blocking consequence. Workpaper submission history has
mobile cell labels, and long comments and identifiers wrap. The workpaper's
native server-autosave textareas, finding response command, review disposition
command, authorization and same-document scope clearing remain intact. This
slice is `PRESENTATION_ONLY` and `WIRE_EXISTING`; the prototype's extra
workpaper upload, reassignment and review-desk controls need separate real
backend contracts and are not presented as available here. Local verification
is recorded in `status.json`.

---

## UI modernization — audit program and fieldwork

The real audit program library and engagement fieldwork control center now
use the shared audit heading, compact scoped metrics and white panels. The
library's selected version label renders its actual version and stays within
small viewports. Fieldwork initially shows one source section instead of the
entire adopted procedure catalogue, with an explicit All sections choice and
visible count; filtering only changes which authorized rows are displayed.
Published wording, applicability decisions, aggregate conclusions, independent
review and route-scope clearing remain governed by the existing commands and
queries. This is `PRESENTATION_ONLY` plus `WIRE_EXISTING`; it does not add the
prototype's risk-editing or simulated fieldwork actions. Local verification
is recorded in `status.json`.

---

## UI modernization — audit planning and completion

The real engagement audit plan and completion checklist now use the shared
record heading, navigation links, compact counts from authorized rows, white
panels and mobile table labels. The planning forms reflow into two columns on
wide screens and one on narrow screens without changing their fields or
commands. The completion view stacks long representation text and wraps
actions on narrow screens. Its in-place route change now clears the prior
engagement and reloads authorization and scoped data for the new parameter,
with a generation fence against an older load finishing late. This is
`PRESENTATION_ONLY` plus `WIRE_EXISTING` and one route-scope correctness fix;
the counts do not claim release readiness. Prototype-only planning lifecycle,
risk editing and professional decisions remain outside this slice. Local
verification is recorded in `status.json`.

---

## Project task progress display

The administrator tracker now divides each measured task bar into completed,
active or in review, pending or reopened, and blocked segments. The visible
counts and accessible progress description come from the same published task
cards; the overall total still counts shared cards once. Areas without a
published module mapping remain explicitly unmeasured. This is a read-only
display change and does not advance task status or acceptance gates.
The task-card lists now have status filters so administrators can find
completed, active, pending, or blocked cards across modules. The overall and
module bars retain their full published denominators while a filter is active.

---

## UI modernization — accounting period views

The real period detail, roll-forward and restatement routes now use the
shared accounting heading, white panels and responsive workbench layout.
Roll-forward and restatement counts come only from their authorized loaded
rows; their history tables have mobile cell labels. Existing create, reload
and independent-review actions, immutable period lineage, role checks and
draft boundaries remain unchanged. This is `PRESENTATION_ONLY` plus
`WIRE_EXISTING`. Local verification is recorded in `status.json`; the
remaining accounting routes and wider UI acceptance remain open.

---

## UI modernization — mapping and adjustment-journal detail

The real `/app/accounting/mappings/{id}` and
`/app/accounting/journals/{id}` views now return directly to their matching
queues and use compact record headings, white summary/workflow panels and
scoped metric cards. Mapping section links navigate within the existing
record; each mapping table supplies mobile cell labels. The journal metrics
repeat the visible line count and debit/credit sums and do not assert external
posting. The original mapping approval, journal posting and instruction export
commands, exact record IDs, SoD checks, immutable lineage and scope-revocation
clearing remain in their existing services and page handlers. This is
`PRESENTATION_ONLY` plus `WIRE_EXISTING`; exact local test evidence and the
remaining routes are tracked in `status.json`.

---

## UI modernization — accounting record and review queues

The real `/app/accounting/mappings`, `/app/accounting/journals`,
`/app/accounting/differences` and `/app/accounting/reviews` routes now use a
compact accounting heading, scoped count cards, white queue surfaces and
informational empty states. Counts come from each route's existing authorized
rows. "Linked journals" means a persisted proposed-journal link, not a
resolved difference; package selection remains an optional browser draft for
preview only and records no decision. The accounting route navigation, exact
detail links, native review checkboxes, `data-draft-field` hooks and server
authorization remain intact. This is `PRESENTATION_ONLY` plus
`WIRE_EXISTING`; extra prototype review actions and provider-dependent
behavior were not copied. Exact verification and remaining visual work are in
`status.json`.

---

## UI modernization — accounting landing and evidence

The real `/app/accounting` and `/app/accounting/evidence` views now share the
compact accounting heading, metric cards and white workspace panels. The
workflow dashboard's progress bar counts complete rows against only the
authorized visible period rows; required, stale and blocked rows stay open.
The evidence page does not infer approval from a linked reviewed result. The
native context selector and draft scope remain in place. The shared responsive
drawer reopens on a mobile-to-desktop viewport change. These are
`PRESENTATION_ONLY` and `WIRE_EXISTING` changes; the prototype's additional
accounting actions are not available from these views without their real
application contracts. The exact local verification and remaining UI scope are
recorded in `status.json`.

---

## UI modernization — practice and firm finance

The real `/app/practice/leads`, `/app/practice/time` and `/app/finance` routes
now use compact, scoped metric strips and a consistent practice panel rhythm.
Leads and time use the source's pipeline/workbench visual cues without copying
its fake records, persona controls or browser-local business state. The time
task and time-entry forms remain native with their existing form and
`data-draft-scope` boundaries; responsive classes replace only layout styles.
Firm-ledger counts label the records actually returned by its authorized
query, including "recent postings shown" rather than an all-time total.
Repeated explanatory copy was removed where PageHeader already conveys it.
The draft helper's duplicate optional-marker pseudo-element is suppressed
only in forms whose labels already spell out "optional". This is
`PRESENTATION_ONLY` plus `WIRE_EXISTING`; proposal-stage edits, budget impact,
full lifecycle boards and any provider-dependent behavior remain outside this
visual slice. Exact verification and remaining UI work are in `status.json`.

---

## Administrator project task progress

The administrator-only `/app/administration/project-progress` page reads the
published R2R task-card front matter and manifest. It shows one overall bar
counting each task once, seven module cards (Modules 20–26), and separate
audit-workflow and cross-module bars. The audit workflow is also broken out
into four bars for its published foundation, core fieldwork, extended
fieldwork and completion task phases; these are grouped by the manifest's
`AUD-17` through `AUD-20` work packages and sum to the audit aggregate.
Each card exposes the source status, with completed, active,
pending and blocked counts. The page is read-only: status transitions and
evidence remain governed by the task-pack helper and review rules. Its bars
measure reviewed task-card completion, not implemented software or production
readiness. The same dashboard now lists practice leads, time, firm finance,
documents/client portal, durable operations and administration as untracked
application areas with neutral, unmeasured bars. The published task pack has no
dedicated mapping for those areas, so no percentage or invented completion count
is shown. The Web publish includes the tracker snapshot, so a new deployment
is needed to show later repository status changes. Exact verification is in
`status.json`.

---

## UI modernization — client portal views

The three existing `/portal` views now share a client-safe navy header,
compact panels and narrow-screen detail layout. The request detail's native
file control no longer widens the document at 320px; upload/draft attributes
and backend effects are unchanged. The package's existing management state
uses the shared text-and-icon status chip. The client request and package
route-change browser cases are extended with responsive-width checks and
synthetic visual capture. Remaining route-family passes and complete UI
acceptance are still open; exact test outcomes are in `status.json`.

---

## UI modernization — shell and Portfolio pilot

The real Blazor app now has a prototype-inspired navy staff drawer, light top
bar, compact shared theme and workspace surfaces. Route-selected client and
public layouts keep the staff drawer out of client, home, sign-in and setup
views. The Portfolio pilot restyles authorized metrics, search/export and
register panels without changing their queries or actions. The prototype is
read-only visual reference; no persona, scenario, fake data or browser-local
business state has been copied. The route smoke check now asserts shell
separation and responsive document width for Portfolio and the client portal.
The remaining staff and client page families still need route-specific visual
and accessibility review. Exact verification outcomes belong in `status.json`.

---

## AS-PAR-002 portal, group and assessment scope isolation

The sibling-client browser audit now also exercises client portal routes,
consolidation group grants and in-circuit navigation across the assessment
workbenches. The assessment detail route gives an unknown ID and an out-of-scope
decision the same unavailable state. The tests check that prior client content
clears, sibling records remain hidden, and a return to the authorized route
restores its content. Application command isolation and the remaining route
inventory are still open; this does not complete AS-PAR-002. Exact source and
verification outcomes are in `status.json`.

## Development tenant administration and Graph query repair

Microsoft Graph member reads now send the required advanced-query count and
consistency settings, including the user-only member listing. A live invitation
requires an HTTPS redemption destination; the Graph adapter rejects HTTP before
dispatch. An opt-in live Development test exercised user provisioning, managed
group add/remove, guest invitation and the PBC mail worker through the real
providers. Graph accepted the mail request; recipient inbox delivery is not
asserted. The synthetic user, guest and temporary group were removed and exact
tenant reads found none remaining. This is Development evidence, not production
approval. The configured loopback HTTP guest redirect remains unsuitable for
live invitations until an approved HTTPS application destination is supplied.
Exact test and external-gate outcomes are in `status.json`.

## AS-PAR-002 sibling-client differential isolation

A new browser case signs in a user whose grants cover only client A (client- or
engagement-scoped), records the rendered text of every staff list, queue and
search route, then gives a sibling client B marker-named practice, PBC and
accounting records. Every route must render identically and never show the
marker, and client B's detail routes and PBC download must look exactly like a
random identifier. The audit found three pages that confirmed a sibling record
existed by showing "access denied" instead of "not available" (engagement PBC
inbox, audit fieldwork, staff financial package); they now render one uniform
unavailable state. At this slice's time, client portal and consolidation group
grants were still untested; the newer section above records their differential
cases. Command-level isolation remains open, so AS-PAR-002 stays partial.
Exact verification is in `status.json`.

## P2 durable worker delivery and P2b isolation matrix

A client portal upload now reaches SharePoint through the real pipeline: in a
Playwright journey the client staged a synthetic PDF, staff completed it on a web
host with `PbcTransfer:LiveProvider`, which enqueued a LIVE `pbc` operation, and a
separately started Acceptance worker process delivered it to the Development
selected site; the intent became RECEIVED and the exact stored version re-read
with the staged SHA-256. The drive now also rejects a token issued for a tenant
other than the binding's.

The P2b matrix runs automatically against the fake Graph drive: client A/B
documents land in separate client folders and neither scope can read the other's
receipt; mixed or guessed client, engagement, intent, drive and item identifiers
fail closed; a wrong-tenant token or connection blocks before any write; revoked
client and administrator identities are refused; a 429 moves the operation to
RETRY_WAIT and later completes without duplication; a lost final response is
reconciled by exact name to RECEIVED; a failed commit reconciles to
PROVIDER_BLOCKED with the request still open. Live in Development, client A/B
delivery and separation, guessed identifiers, and both the local and Microsoft
Entra wrong-tenant refusals passed, and all synthetic folders were deleted.
Production approval and independent review remain. Exact verification is in
`status.json`.

## P2 selected-site PBC provider

A firm-wide Administrator can now provision, from Administration → Microsoft 365
→ SharePoint, each accepted client's workspace folder tree and each engagement's
folder tree inside the approved selected site. The engagement's PBC intake folder
becomes a `working` repository binding, and its capability row is `VERIFIED`
only after a disposable upload, exact SHA-256 read-back and delete succeed.
`GraphPbcProviderSink` now has a live composition: it resolves only that
verified binding, uploads via a Graph upload session in 320 KiB-aligned
fragments through the restricted pre-authenticated transport, and issues a
receipt only after the exact stored version reads back with the staged SHA-256
and size. Unknown outcomes reconcile by the deterministic per-intent file name;
a retry never overwrites. The Web host enqueues LIVE transfers only with
`PbcTransfer:LiveProvider`, and only an isolated Acceptance worker with external
effects and group `pbc` may claim them. The resolver no longer requires the
client workspace root to equal the binding root; the tested-binding fingerprint
pins the exact folder.

A live Development run of the new acceptance test provisioned a synthetic
client and engagement, verified the binding, uploaded one synthetic document with
a matching exact-version receipt, reconciled it by probe, refused an overwrite and
deleted the synthetic folders. At the time of this section, a real client-staged
document had not yet been transferred by the durable worker; that was later
observed and is recorded in "P2 durable worker delivery and P2b isolation matrix"
above. Production approval remains. Exact verification is in `status.json`.

## Microsoft 365 tenant connection and unified administration

The tenant connection flow now completes the consent contract. After the
state-bound admin-consent return, a second nonce-bound OIDC sign-in to the
separate consent app authenticates the consenting tenant administrator; the
code is redeemed server-side, issuer/audience/tenant/nonce are validated,
external and personal identities are refused, and only the administrator's
`tid`/`oid` are stored. This trusted path is the only way to set the
connection's consent state to `VERIFIED`, which activation already requires.
Each enabled capability is then verified separately (`VERIFIED`,
`NOT_GRANTED`, `FAILED`, `BLOCKED_EXTERNAL`; stale after 24 hours) with its own
single-role app identity.

Optional capabilities are implemented and disabled by default: `User.Create`
tenant user provisioning (wizard with a one-time, never-stored initial
password), `User.Invite.All` guest invitation bound only to a CLIENT/ENGAGEMENT
ClientUser grant, and `GroupMember.ReadWrite.All` administration of allowlisted,
non-role-assignable groups that never grant AuditSphere access. Every Microsoft
mutation runs through `m365_external_operations` with an idempotency key,
recheck before dispatch, UNKNOWN reconciliation by immutable identity and a
separate local binding transaction; every change writes an append-only
`m365_administration_events` row. Role assignment now has a reviewed dialog with
current/proposed access, capability diff, scope expansion/reduction and
independence impact, explicit expansion confirmation, GROUP scope, reasons and
expiry enforced by real revocation. The Administration page is a tabbed
workspace with a dashboard and setup progress derived from persisted state.

CI and local browser journeys also use the Development/Test-only simulated
tenant. Separately, the authorized Development tenant completed the live
administrator-consent and nonce-bound identity path with a distinct
certificate-backed directory-reader registration. Consent and the bounded
directory capability are `VERIFIED`. The selected-site capability is also
`VERIFIED` after exact saved site/library/root reads and a denied synthetic
unrelated-site control; the local working-site binding is `ACTIVE`. Optional
mail, provisioning, guest and group capabilities remain separately gated; no
successful Microsoft mutation or production acceptance was observed.
The canonical GitHub Wiki guide covers both exact callbacks and independent
capability gates. Exact verification is in `status.json`.

The setup progress checklist now shows trusted consent for the exact draft
and current connection revision. The administrator resumed the local
Development draft without reentering the bootstrap proof. The provider test
read the saved SharePoint site, library and root, then received `403` from
the separately confirmed synthetic unrelated site using the selected-site
identity. The negative-control URL is held in private Web user secrets. The
provider path recorded exact-revision resource evidence and marked the draft,
connection and selected-site capability `VERIFIED`. A failed recheck now
supersedes an older pass.

The default client folder template version 1 was saved and approved locally.
After the owner approved the exact Development working-site binding, the
administrator used the guarded activation action. The setup page displayed
draft state `ACTIVE`, revision 11, and “Workspace activation recorded.” This
is the local default binding for future client workspaces. A reload exposed a
post-activation resume guard that rejected the same bound administrator; the
guard now permits that administrator to reopen the active configuration for
review while draft saving stays disabled. The Development browser showed the
same `ACTIVE` revision 11 and activation checklist after restart and resume.
Mail, records, production release and independent acceptance remain separate.
Exact tests, hosted checks and live Development observations are in
`status.json`.

The tenant-connection directory search now accepts an optional exact
user-principal-name domain. A domain alone browses enabled users; a name
prefix plus domain uses one bounded Microsoft Graph query. Server-side domain
validation and a post-response suffix check prevent a cross-domain result
from being offered for binding. The Development tenant returned the expected
member for the combined filter and no match for a different domain. This
read-only search grants no AuditSphere role.

## AS-PAR-002 audit program library access refresh

The audit program library now clears cached versions and procedure text before
an explicit refresh resolves the current actor and grant. Browse and Search
hide the old projection while reauthorizing and leave it cleared on denial.
The Application library and section
queries recheck firm-wide internal authorization after reading, so a grant
change during a read cannot return the protected result. A PostgreSQL-backed
browser regression opens a published procedure, revokes the exact Partner
grant, and confirms Search and Refresh leave no procedure text visible. This
is one bounded authorization surface, not completion of the whole route audit;
exact verification and remaining limits are in `status.json`.

## Microsoft 365 setup: one-click administrator resume

The tenant connection page now prepares a one-use, ten-minute consent state
bound to the current firm-wide Administrator, exact configured tenant, setup
draft, and session epoch. A separate consent app registration and exact callback
must be explicitly configured and enabled. The callback consumes the state
once and records `RETURNED_UNVERIFIED` only after the tenant matches; it does
not activate a connection or assert Microsoft permissions. A bounded
Development directory check is described below. The current second sign-in leg
authenticates the administrator and verifies the consent in Development. The local migration and
focused PostgreSQL and URL-builder checks are recorded in `status.json`.

The separate directory reader now supports bounded, administrator-only live
search from the tenant connection page, with a dedicated certificate and
`User.Read.All` application role fence. It is disabled by default. Search
returns no local role grant. A separate **Verify directory access** action
performs a bounded exact-member provider read and records its result against
the current connection revision, with a 15-minute recent-check display.
This does not verify the Microsoft consent grantor, activate the connection,
or verify any other capability. A separate explicit Bind exact identity action
re-reads the selected member before creating or refreshing its local binding.
In the Development tenant, the dedicated app received the exact permission,
and a certificate token plus the live page completed bounded Graph reads.
The consent callback's denied path also returned to the app and consumed its
attempt without activating a connection. The later verified live callback is
recorded above; neither observation establishes production acceptance.
The binding step re-reads a selected Microsoft member by exact object ID.
Binding is idempotent and records an enabled Graph
observation without a role grant. The Administration handoff starts with no
role or scope chosen; the existing scoped role service remains the separate
access transaction. Guest invitations and tenant user creation are not
enabled by this path. A live Development browser check bound the already known
synthetic Staff fixture and reached the role review form with blank role and
scope choices. No new grant was applied. The later Development consent return
is verified as described above; this is not production acceptance.
The User and role administration screen now offers a paged selector of enabled
Microsoft users when the separate directory reader is configured. An optional
domain suffix filter is applied in Graph with an advanced query, and the app
rechecks tenant, enabled state and exact domain on every page. The roster
fields remain in a collapsed fallback disclosure. The Development browser
showed both unfiltered and filtered results, then bound the existing synthetic
Staff fixture without changing its grant.

The firm administration screen now projects active local user, administrator,
grant and pending-first-access invitation counts from the current firm. Its
Microsoft 365 overview keeps the local connection revision, consent record,
recent directory check, selected-resource evidence, and mail/group blockers
separate. An unverified consent callback is not shown as a verified grant.
The current blocked states and test evidence remain in `status.json`.
The local configuration service now refuses administrator-entered tenant
consent `PASS` evidence and cannot promote recorded references to a verified
connection. Activation requires a separately verified consent state, including
for older revisions with `OBSERVED` evidence. The later Development consent
path verifies the authenticated administrator and independently checks the
directory reader. The setup page still labels historical provisional consent
rows as unverified.
When an exact-app-matched directory reader is configured, a successful
one-use consent return now runs the bounded exact-member directory check
before returning to the tenant page. The check records directory capability
evidence only; the callback was `RETURNED_UNVERIFIED` at that earlier revision
and could not identify the Microsoft grantor or activate the connection. The
current second sign-in leg resolves that identity in Development. A denied return
does not run the check. The callback passes its original setup draft identity
to the directory check, so a newer draft for the same firm and tenant cannot
receive evidence from an older consent attempt.

The owner has requested a tenant-administration experience that supersedes the
earlier blanket Graph-scope exclusion only for an isolated read-only directory
reader. The [permission decision](../architecture/auditsphere-m365-tenant-administration-permissions.md)
records `User.Read.All` as the proposed application permission for that reader,
separate from the `Sites.Selected` document worker. A dedicated Development
registration has this permission and completed a bounded live read; production
consent and acceptance remain open. User provisioning, guest invitations and Microsoft
group changes remain optional, separately gated capabilities. Existing local
`RoleGrant` administration remains the source of AuditSphere access.

After the initial proof-backed administrator binding, the same currently
authorized firm-wide administrator can reopen the local Microsoft 365 setup
draft with **Continue setup with this account**. The button uses the current
Microsoft `tid`/`oid` and local grant; it does not ask for the bootstrap proof
again. For an empty tenant field, the authenticated tenant ID is saved to the
draft automatically. The screen shows the tenant and working-site URL first;
resource IDs, optional capabilities and folder templates are under advanced
settings. First-time bootstrap still requires the private proof. This local
draft action does not grant Microsoft consent, selected-site access, or live
provider readiness. Exact test evidence is in `status.json`.

The resumed draft now displays a compact connection-progress checklist. Its
Application query checks the current firm-wide Administrator grant, reads only
this firm's draft, linked connection revision, recorded exact-resource evidence,
approved client template and workspace activation, and labels each step as a
saved local record or an outstanding operator action. The checklist links the
canonical production guide; it never probes Microsoft or reports a prepared
reference as live access. Exact verification is in `status.json`.

## P2 selected-resource read preflight

The Infrastructure provider now has a read-only Graph probe for an already
active firm workspace. It loads the target from stored configuration, requires
the matching active connection and approved client template, obtains a token
through a separate runtime certificate reference, and accepts only a token for
the expected tenant carrying the single `Sites.Selected` application role.
It reads the exact site, enumerates libraries only within that site, and checks
the configured root folder identity and URL. Mismatches and missing bindings
block before any write; throttling is retryable. (Historical: at the time of this
section the probe was not composed into the Worker and had no live tenant result;
the later "P2 selected-site PBC provider" and "P2 durable worker delivery" sections
supersede this.)
The local test evidence and remaining gates are in `status.json`.

The same read-only check can now run against a pending, exact-firm setup draft
before workspace activation. It takes the tenant, site, drive and root IDs
from the saved draft and its separate runtime credential reference, then uses
the same `Sites.Selected` checks. Suspended, blocked and active revisions are
not eligible for this pre-activation path. A successful observation does not
record consent/evidence, activate the workspace or enable external effects.
Those two prerequisites must be present in each environment before a live
tenant result can be claimed.

The authorized Development tenant now has a separate certificate-backed
runtime credential on the existing application. A live token carried only
`Sites.Selected`; Graph read the approved site and its site-scoped libraries
and roots, while an existing synthetic unrelated site returned `403`. The
observed `Client Content` binding was saved in the local draft, and the
pre-activation probe passed against that stored binding. No upload,
exact-version readback, retry/reconciliation, production access, activation,
or independent acceptance was observed. The Worker still has no live PBC
provider composition.

The firm-wide administrator's Microsoft 365 setup page now offers **Test
selected site connection** for a saved draft with a prepared connection.
The action checks current local authorization before and after a read-only
Graph probe of the saved site, library and root using the separately configured
runtime certificate. Its result is a transient screen message: it records no
consent or selected-resource evidence and does not activate the connection.
The private Development certificate paths are held in .NET user secrets, not
repository settings. Exact local verification and remaining P2 gates are in
`status.json`.

A selected-site application token in the approved Development tenant also
created an upload session against the saved library and root. The session was
canceled before any bytes were committed. This confirms one bounded write API
is available to the existing site grant; it does not verify a document upload.
The new `PbcRepositoryBindingResolver` refuses an operation unless its trusted
firm, client, engagement and intent match a single current repository binding,
ready client workspace, active firm connection and a capability record tied to
the exact binding fingerprint. It is a local prerequisite component only:
neither the Worker nor `GraphPbcProviderSink` uses it yet, and no live PBC
transfer, exact-version readback, or reconciliation is accepted.

The next P2 prerequisite is a restricted transport for Graph-issued
preauthenticated upload/download URLs. It accepts only HTTPS commercial
SharePoint/OneDrive hosts and GET, PUT or DELETE; it sends no Graph bearer
token, rejects redirects, suppresses URL-bearing HTTP telemetry, and replaces
transport exceptions with a non-sensitive error. A disposable synthetic file
was uploaded to the approved Development site; Graph returned item metadata
and a current version record, then the file was deleted. This does not prove
preserved exact-version retrieval or a durable application receipt. The live
PBC adapter remains uncomposed.

## P1 immutable identity lookup

The Web actor resolver and sign-in landing now require the Entra `oid` claim
alongside `tid` and the signed-in session epoch. A matching `sub` or name
identifier can no longer substitute for the immutable object ID. A
PostgreSQL-backed API regression checks missing `oid`, changed email and wrong
tenant behavior. The development app registration was inspected in the
authorized tenant and already contains the local web callback; no tenant
setting was changed. This is a local identity boundary, not live OIDC or
production acceptance. Exact verification is in `status.json`.

The application cookie now requires the Secure flag outside Development/Test,
and remains HttpOnly. The local profiles retain request-matched cookie security
for the supported HTTP test harness. The production deployment Wiki already
requires an HTTPS hostname and exact HTTPS callback, so its operator steps
remain accurate.

## AS-PAR-002 firm Finance current-access refresh

The firm Finance workbench now reads periods, accounts, recent postings, and
close-period capability through `FirmFinanceQuery`, which authorizes before and
after the scoped read. Explicit refresh clears the old ledger and close draft,
then rechecks actor/session epoch and fences late reads. Period close continues
through its guarded Application command and reloads current access after a
successful decision. A browser regression covers same-document grant revocation
and row removal. This remains one bounded authorization slice; exact checks are
in `status.json`.

## AS-PAR-002 practice lead current-access refresh

The practice-leads workbench now reads a bounded firm-wide commercial list
through `PracticeLeadQuery`, which rechecks the grant after reading. Explicit
refresh clears prior lead rows and form drafts, resolves the current actor,
and fences older reads by generation and session epoch. Lead commands retain
the existing guarded Application service and reload the protected view after
success. A browser regression covers same-document grant revocation and lead
removal. This is a bounded authorization slice; exact results are in
`status.json`.

## AS-PAR-002 Operations current-access refresh

The Operations workbench now clears its redacted ledger, operating mode,
command result, and cancellation draft before an explicit refresh checks the
current firm-wide administrator grant. A second access check and session-epoch
check fence late reads. Recovery commands resolve the current actor and reload
the protected view after the guarded Application command. A browser regression
revokes the administrator grant and confirms that a same-document refresh
removes the previously visible operation. This is a bounded authorization
slice, not completion of the whole-application audit. Exact verification is in
`status.json`.

## R2R baseline scope and inventory

The owner approved the STE-R2R-ARCH-001 seven-module scope. T001 now has a
[current-code inventory and conflict register](../task_breakdown/tracking/auditsphere-r2r-tracker-current-baseline-inventory.md)
that identifies reusable accounting and consolidation symbols, distinguishes
proposed shared contracts from implemented equivalents, and records the current
Purview/eSignature exclusion without removing release or records evidence gates.
The repository owner accepted the exact T001 inventory commit; this owner
decision does not satisfy the separate non-owner independent-review requirement.
T002 direction is approved, but its transaction and contract handoff remains in
review. The separate GitHub Wiki deployment
guidance was published under the owner's authorization and its canonical pages
were checked after publication. Exact source and Wiki revisions and observed
checks are in `status.json`.

## R2R chart-revision transaction fence

Chart publication and draft account/alias edits now hold the same chart-version
row lock through their guarded save. A paused draft edit cannot land after
publication, and a second publisher sees the committed protected state. The
PostgreSQL race tests, full solution suite, EF model check and hosted CI passed;
exact results are in `status.json`. The T002 ledger now identifies this owner
and maps current setup and intake methods through T020 while leaving proposed
contracts and independent review explicitly open.

## R2R chart-version allocation and ownership map

Chart-version creation now locks the client row through version allocation and
save, so concurrent requests receive successive versions. A PostgreSQL race
test proves the ordering. The T002 ledger now names a current Application owner
or an explicit new-contract decision for every preserved request; that mapping
does not prove the proposed contracts are implemented. T002 remains in review
until its outstanding contract choices and independent acceptance are resolved.

## AS-PAR-002 firm administration current access

Firm administration now loads its grant directory, invitation evidence, directory
observations, access history, and safety state through a named Application query
that checks the current firm-wide Administrator grant before and after the read.
Explicit refresh clears the prior projection and private form values before
reauthorizing; late reads cannot republish an older result. Invitation copying
also rechecks both current administrator access and the invitation's active
grant before using the clipboard. The browser regression revokes the invitation
grant and verifies that a stale copy action does not reach the clipboard; it
then revokes the administrator grant and verifies that the same document clears
its private directory on refresh. This is a bounded AS-PAR-002
slice; exact verification is in `status.json`.

## AS-PAR-002 staff portfolio refresh and export

The portfolio CSV now prefixes spreadsheet-formula-shaped text with an apostrophe
before CSV escaping; the existing browser export regression uses a synthetic
client name beginning with `=` to verify the downloaded payload. Numeric
metrics remain display-only and no source identifier is rewritten. Exact
verification is in `status.json`.

The staff portfolio now clears cached clients, metrics, release candidates, and
financial packages before an explicit scope refresh. CSV download performs a
fresh scoped read and current-grant check before creating the file, so an open
page cannot export its previously rendered rows after local grant revocation.
Overlapping reads cannot republish an older result. The browser regression
checks the authorized CSV payload and then revokes the grant in the same
document; the next export command clears the private row and sends no second
download payload. The existing client PBC same-document route regression also
exposed a missing parameter-change reload; the request page now reloads by
route parameter and fences late reads before publishing protected content.
Upload and reply commands pin their starting request and page generation,
then reauthorize before displaying refreshed conversation or transfer state.
This slice does not close the whole-application AS-PAR-002 audit. Exact local
verification is in `status.json`.

## AS-PAR-002 proposal current-access refresh

The proposal detail page now offers an explicit refresh of the current proposal.
It clears the displayed terms and version list before resolving the actor and
checking the current firm-wide commercial grant again. A browser regression
loads a proposal, revokes that grant, refreshes in the same document, and
checks that the prior proposal details disappear. Route-change and scoped-user
denial checks remain in the same journey. This is a bounded read/revocation
repair; it does not enable the proposal review, delivery, or client-response
workflow and does not close the whole-application authorization audit.

## Local development schema readiness

The persistent loopback development database was backed up and restored in a
temporary rehearsal before its pending repository migrations were applied. The
Web host was stopped for the migration and restarted afterward. Its readiness
endpoint is healthy, the original administrator binding and inactive setup
draft remain intact, and a second restore rehearsal passed against the updated
schema. This is local development readiness, not production migration or
provider acceptance. Exact schema and verification facts are in `status.json`.

## P1 explicit Microsoft account selection

The OIDC sign-in route now accepts an opt-in account-choice flag for testing
separate tenant fixtures in a browser that already has an administrator Microsoft
session. The default challenge remains unchanged. The operator Wiki explains
the optional route; exact checks and source identity are in `status.json`.

The account picker resumed the existing administrator session when its "Use
another account" option was chosen. An additional opt-in reauthentication flag
now asks Microsoft to show a fresh sign-in form. The local browser reached the
staff fixture's password screen; the fixture credential and any MFA remain with
the user. Neither the account-choice prompt nor the password screen alone proves
a fixture sign-in, local binding, or grant. The operator Wiki describes the fallback,
and exact checks and revisions are in `status.json`.

A fresh browser challenge using the already signed-in developer administrator
completed Microsoft's OIDC callback into the local Web app. The portfolio and
firm administration pages loaded, and the latter displayed the bound firm-wide
Administrator grant, Development environment, and fenced external effects.
Readiness returned healthy. The protected Microsoft 365 setup page still
requested its private bootstrap proof, so no setup revision or provider binding
was changed. This positive local administrator readback does not establish
staff/client isolation, wrong-tenant denial, or production P1 acceptance.

The staff fixture subsequently completed Microsoft sign-in on the local
Development host. AuditSphere rejected the unmapped identity, as intended, but
initially exposed that rejection as a server exception. The callback now shows
the existing access-not-assigned page for this case and a generic response for
other remote authentication failures. The same staff fixture reached that page
after the fix; it had no local role or application access. The administrator
subsequently bound the exact enabled Microsoft tenant/object identity through
the local roster form. A guarded local transaction then created one synthetic
PROSPECT client and its safety state as a narrow test scope, without fabricating
a commercial approval. The administration form is prepared for a Staff grant
limited to that client. After exact owner confirmation, the administration UI
saved one client-scoped Staff grant and a non-sent invitation intent; a database
readback confirmed no firm-wide or engagement grant for that user. A subsequent
account-picker attempt showed the synthetic client on the portfolio, but the
shared browser session also retained administrator controls, so that first
attempt was inconclusive. The owner then completed a fresh Microsoft staff
credential challenge. The resulting portfolio listed the granted synthetic
client, firm administration returned Access unavailable, and a separately
seeded synthetic sibling client remained absent from the portfolio and search.
These are local Development UI checks, not complete P1 or production acceptance. The
remaining P1 fixture cycle and production acceptance remain open; exact checks
are in `status.json`.

Two separate enabled client fixtures are now bound by immutable Microsoft
tenant/object identity in Development. The firm administration UI saved one
`ClientUser` grant for each: Client X to the original synthetic client and
Client Y to the sibling, with unsent invitation intents. Read-only PostgreSQL
readback found exactly one active grant per fixture and no firm-wide or
engagement scope. Neither client fixture has a recorded first access yet.
Live client sign-in, cross-client denial, negative identity fixtures, and
production acceptance remain open; the observed grant details are in
`status.json`.

The owner subsequently completed Client X's Microsoft sign-in and required
Authenticator registration. The browser reached the restricted client portal;
the app recorded first access for Client X but not Client Y. Direct navigation
to operations and firm administration from that client session showed Access
unavailable. There were no assigned requests or shared packages, so the
cross-client data-isolation fixture remains untested. Client Y and negative
identity cases are still pending.

The owner also completed Client Y's Microsoft sign-in and Authenticator
registration. Its browser returned to the restricted client portal, and firm
administration showed Access unavailable. Database readback now shows first
access for both client fixtures and exactly one active `ClientUser` grant per
fixture, each tied to its own synthetic client. Both portals have no assigned
records, so cross-client content denial and the remaining negative identity
fixtures are still pending.

Focused PostgreSQL-backed browser tests separately verified local sibling PBC
denial by direct URL and exclusion from an engagement-scoped portal read.
Focused identity regressions verified immutable `tid`/`oid` lookup despite a
changed email, wrong-tenant rejection, and disabled/stale-session denial. The
later Development fixture below adds live client-content checks. Exact rerun
results and source revision are in `status.json`; P1 remains open.

The first synthetic Draft PBC request exposed a client-visible gap: before the
fix, a fresh Client X Microsoft sign-in could open that unsent request. The
client inbox and direct request page now exclude Draft state. After restarting
the Development host, Client X's direct Draft URL returned Request unavailable.
A browser regression also confirms that an owning client cannot find its own
unsent Draft in the inbox or by direct URL.

A separate clearly marked synthetic request passed the application service's
Draft-to-Sent transition. Its notification remains queued locally with no
delivery timestamp; external effects are disabled. Client X's portal displayed
only this Sent request and opened its details. A fresh Client Y Microsoft
sign-in showed no assigned request and direct navigation to the Sent request
returned Request unavailable without its private marker. This completes one
local Development positive/negative content-isolation fixture, not the other
live identity, production or independent-review gates of P1.

A focused Release E2E rerun also passed for a synthetic owning client opening
its assigned PBC request and completing the guarded upload journey (1/1).
This separately verifies the local positive browser path in an isolated test
database; the Development Sent fixture observation above is distinct.

The synthetic Client Y sign-in name was temporarily changed in the developer
tenant. After the owner completed a fresh Microsoft credential challenge under
that name, AuditSphere still resolved the same client-scoped identity: its
portal listed no Client X requests and the direct Client X Sent-request URL
returned Request unavailable. The administrator restored Client Y's original
sign-in name and Microsoft 365 confirmed the reverse update. The local user
email display value did not synchronize during the changed-name sign-in; the
immutable subject remained the authorization binding. Wrong-tenant and
disabled/revoked live fixtures, production OIDC and independent P1 acceptance
remain open. Exact observations are in `status.json`.

## P8a production Data Protection key ring

Production Web startup now also requires a persistent Data Protection key
directory and a currently usable certificate with a private key. The key ring
is certificate-protected at rest, survives a Web host restart, and uses the
configured tenant and installation IDs for application isolation. Synthetic
production-host regressions cover a missing profile, encrypted key XML,
restart readback, and cross-installation/tenant rejection. The existing Wiki
deployment guide names the required private configuration. Actual secret
custody, backup, certificate rotation, restore, and production multi-instance
operation still need deployment evidence; P8a remains open. Exact source,
local checks, hosted CI, and Wiki revision are in `status.json`.

The production profile now accepts explicitly configured prior certificates
for decryption while using the current certificate for new key protection.
An incomplete prior entry refuses startup. A synthetic host test confirms an
old protected value remains readable through a certificate change, and becomes
unreadable if its prior certificate is omitted. The Wiki has administrator
rotation steps. This proves local configuration behavior; no production
certificate, secret store, key backup, or rotation rehearsal was used.

The Web host now gives each HTTP request a server-owned diagnostic ID in its
response header, Serilog scope, and active trace. It ignores a caller-supplied
ID; a focused API regression covers distinct IDs on success and missing-route
responses. Existing durable operations retain their separate persisted
correlation IDs. Propagation into every browser command, audit event, provider
request, dashboard, and alert remains outside this local slice.

The Web telemetry pipeline now registers the framework's HTTP request meter
for duration and active-request measurements. A configured OTLP endpoint is
validated once at Web or Worker startup; malformed or credential-bearing URLs
refuse startup instead of silently disabling export. An absent endpoint still
disables export. Local configuration tests do not establish a live collector,
dashboard, alert threshold, or production telemetry privacy acceptance.

The worker's existing durable-operation disposition counter now includes the
persisted destination state as a bounded tag. Retry waits, uncertain results,
and dead letters can be distinguished without using operation, client, or
correlation identifiers as metric labels. Database-backed regressions cover
retry and uncertainty transitions. This does not create a dashboard, alert
policy, live exporter result, or production telemetry acceptance.

## P8b synthetic accounting workload baseline

The existing PostgreSQL-backed accounting benchmark was run repeatedly against
its isolated synthetic schema. It exercises concurrent durable enqueue,
two workers processing completeness operations, a paged general-ledger read,
and a pure consolidation calculation. Raw measurements and fixture sizes are
recorded only in `status.json`. This is a local component baseline, not a
concurrent staff or client-portal load test, a large import/upload test, a
measured p95 screen target, or production capacity acceptance. The approved
pilot workload and external-provider failure profile remain outstanding.

## P1 production OIDC startup guard

The Production Web host now refuses startup when OIDC is absent altogether,
including when no `Identity` section is supplied. Development and Test retain
their supported local profiles. The Production cookie-policy test uses synthetic
OIDC settings to exercise the cookie behavior without a live sign-in. The full
local suite and EF model check passed; this closes a configuration gap but not
the live P1 identity fixture cycle. Exact evidence is in `status.json`.

## P1 incomplete OIDC configuration guard

The Web host now refuses startup when any `Identity` setting is supplied without
the tenant ID, client ID, and client secret together. This prevents a partial
development or deployment configuration from silently selecting the no-OIDC
sign-in path. A focused API host regression covers each missing-key shape.
This is local fail-closed configuration behavior; no live Entra sign-in or
production identity acceptance is claimed. Exact verification is in
`status.json`.

## R2R architecture and group authority

T002 remains in review. The repository owner approved the architectural
direction at exact commit `779480c613b49f0e6ee1a818864ef48b5b8bd607`.
Its [architecture and transaction ledger](../task_breakdown/tracking/auditsphere-r2r-tracker-architecture-transaction-ownership.md)
records the current static-service design, preliminary transaction owners and
shared contract candidates. Group creation now derives the creator's group role
from an active firm-wide grant, preventing a Manager creator from receiving an
implicit Partner group grant. Group creation also serializes against firm-role
revocation through the firm safety row lock, so a revoked role cannot create a group after
the revocation commits. The concurrent PostgreSQL regression and full-suite
results, including one initial browser timeout and clean standalone browser
rerun, are recorded in `status.json`. The 111-request registry assigns one
owning task per request; each downstream task must prove its concrete method
and transaction. T002 completion still requires the ledger's explicit transaction-owner,
shared-contract and professional-role decisions; the owner's direction approval
does not accept that incomplete handoff.

Perimeter approval now refuses a review by the scope version's creator, even
when that person has a current group reviewer grant. PostgreSQL fixtures use a
distinct preparer and reviewer; focused and complete Domain/API results plus
the later standalone 75/75 browser suite are recorded in `status.json`. This
closes one current-code independence gap, not
the T041 task or the separate non-owner repository review gate.

## P1 development OIDC credential store

The Web project now declares a stable .NET user-secrets ID, so an approved
non-production `Identity:ClientSecret` can be supplied privately without
editing `appsettings.json` or committing the value. The developer tenant's
existing AuditSphereOps Development registration was inspected read-only: it
has the local `/signin-oidc` web redirect and granted `Sites.Selected`
application plus `User.Read` delegated permissions. That portal observation is
not a live OIDC login test, a selected-site provider test, or production
acceptance. A private credential reference and approved staff/client/negative
identity fixtures are still required before P1 can be exercised. Exact checks
and external gate state are recorded in `status.json`.
The signed-in EasyGuide directory currently shows no Azure subscriptions
accessible to this account, so subscription-backed development infrastructure
cannot be provisioned there yet. Entra app configuration remains separate.

The first-administrator OIDC callback admits only the exact configured
tenant/object identity to proof-backed setup before a local user exists. That
setup-only cookie has no session epoch and cannot resolve a protected application
actor. After explicit owner confirmation, the private proof was submitted to
the local setup route and the initial firm-wide Administrator was bound. A
fresh Microsoft sign-in then reached the portfolio and administration views
with the current session epoch. The setup page reports the binding as complete.
The saved tenant draft remains inactive and unverified, with no selected
SharePoint binding or external effects. The complete identity fixture cycle and
production P1 acceptance remain open. Exact verification and source identity
are in `status.json`.

## P2 provider transfer scope

The durable PBC transfer handoff now carries the operation's firm, client,
engagement and upload-intent IDs into upload and reconciliation provider calls.
Staged chunks are selected using the same scope, and the simulation adapter
rejects a receipt identity for a different intent. This prepares a bounded
selected-resource adapter without enabling a live Graph effect. The live
adapter, approved binding and tenant isolation evidence remain open under P2;
exact local checks are in `status.json`.

Reconciliation now applies the initial transfer's receipt checks to a retried
provider result: the registration identity must be present and match any
previously known identity, and the reported byte count must equal the staged
declaration alongside the existing SHA-256 comparison. The focused PBC tests,
Release build and EF model check passed. A concurrent full solution run had an
unrelated browser failure in a test under active edits and was cancelled; the
exact limitation is recorded in `status.json`.

## Client portal landing access refresh

The portal landing page now clears its assigned request and package lists before
an explicit refresh resolves the current client identity and active grants. It
builds the new projection from an actor-local query, rechecks the session epoch
before publishing the result, and fences overlapping loads. The existing
engagement-scope browser journey now revokes the client's grant and verifies
that refresh removes the authorized request in the same document. This remains
a bounded AS-PAR-002 read/revocation slice; exact checks and source identity
are recorded in `status.json`.

## Client financial-package access refresh

The management-review page now reloads on package route changes and offers an
explicit refresh. Each load clears the prior package, actor, decision fields and
result before resolving current identity and querying the exact package scope;
an older asynchronous route read cannot repopulate the new view. A browser
regression covers grant revocation followed by read-only refresh, alongside
the existing route-isolation and denied-decision journeys. This is a bounded
AS-PAR-002 repair, not complete client-portal or whole-application acceptance.
Exact verification and source identity are recorded in `status.json`.

## Client PBC request access refresh

The client request page now clears its request, transfer receipts, conversation,
and action results before an explicit refresh resolves the current identity and
reauthorizes the exact assigned request. Denied refresh also removes browser-saved
reply and upload drafts. A browser journey exercises same-document navigation to
another recipient's request, return to the assigned request, then role-grant
revocation and refresh without a document reload. This is a bounded AS-PAR-002
read and revocation repair; the whole-application authorization audit remains open.
Exact verification and source identity are recorded in `status.json`.

## Staff PBC inbox route transition

The staff PBC inbox now clears rendered request metadata as soon as navigation
leaves its current engagement route and renders a loading state before an
asynchronous actor/scope reload. A sibling-engagement route cannot leave the
previous request visible while the new authorization decision is pending. The
existing browser regression passed after the repair; the initial full run's
failure and the later standalone browser result are recorded separately in
`status.json`. A route-generation fence also discards late actor, recipient,
reviewer and request results from an earlier navigation. The complete Release
suite passed after that follow-up; exact counts and source identity remain in
`status.json`.

## Release candidate access refresh

The release workbench clears its candidate, checkpoint, attestation, signature
projection and release key before a route change or explicit refresh rechecks the
current actor and exact candidate scope. A late read from an older route cannot
restore its prior evidence. Scope/session denial on issue reloads the protected
view. The release page describes provider evidence without claiming Purview
acceptance, which is outside the approved product scope. A browser regression
covers a same-document route change and a revoked staff grant. Exact verification
results are in `status.json`; this is a bounded AS-PAR-002 authorization repair,
not complete whole-application acceptance.

## P1 bound setup session renewal

The first administrator can resume an expired, bound Microsoft 365 setup session
with the original private proof. The Application service rechecks the exact bound
Microsoft identity, current local session epoch, and active firm-wide Administrator
grant before rotating the setup capability. Missing or stale authority remains denied.
A live check in the local development app resumed an expired session and preserved
the inactive draft without adding another user or grant. This establishes local
recovery behavior only; provider and production acceptance remain open. Exact
verification and source identity are in `status.json`.

## P1 local identity session binding

OIDC sign-in now requires a mapped, enabled local `(tenantId, objectId)` identity and
records its current local session epoch in the protected authentication ticket. The
development sign-in records the same epoch. Current-actor resolution rejects a ticket
whose epoch no longer matches the local user, so an old cookie cannot acquire the new
epoch after role revocation. The browser regression verifies that a refreshed journal
and review queue clear protected data when that happens. This corrects the earlier
review-ledger inference that incrementing the database epoch alone invalidated an
open circuit; the ticket needed to retain its sign-in epoch.

This is local P1 hardening, not live Entra acceptance. Approved runtime credentials,
tenant authorization, identity fixtures and observed live sign-in behavior remain
external prerequisites under issue #13. Downstream live-provider issues remain gated
by that acceptance. Exact checks and source identity are in `status.json`.

The owner kept the Purview and eSignature provider exclusions. The dedicated
provider issues #17–#19 were closed as not planned, and the recovery, operations,
and final-acceptance issues retain their in-scope work without those provider
prerequisites. The issue closures do not establish live records or signing acceptance.

## Period workbench access refresh

The restatement and roll-forward selection loaders clear prior projections and resolve
current actor/grant state before rebuilding scoped choices and history. Revoked client
access and disabled users clear client names, periods, package choices, draft values and
review actions. An engagement-only grant cannot restore client-level period access.
Scope/session-denied command results also clear the protected view. A stale source
package or restatement lineage now remains a visible service error when a fresh
client-scoped authorization check confirms the session is still valid.

`PeriodWorkbenchScopeJourneyTests` exercises the two screens with revoked grants and
disabled identities in an existing browser document and verifies no period/restatement
mutation. This is a bounded AS-PAR-002 AC02–AC03 / AC-24 repair; the whole-application
query, export, command and revocation audit remains open. Verification results and the
source baseline are recorded in `status.json`. Deployment guidance is unaffected.

The pending-work handoff links every story family and preserves optional professional
profile gates and root product exclusions. Follow specification §46 for independent
review and explicit merge authorization before advancing acceptance.

## 1. Active Implementation Scope

- **Active Work Package:** Audit workflow gap-closure, client accounting implementation, and repository-wide documentation & AI navigability refactoring.

- **Implementation Status:** `IN_PROGRESS`

- **Acceptance Status:** `LOCAL_VERIFIED`

- **Scope Boundary:** Import-first preparation, audit, and consolidation workspace (not an operational client ERP). Excludes client sales/purchase/inventory operations, payroll execution, and payment initiation. Purview and eSignature provider integrations are excluded from product scope: exact uploaded signed-document evidence, SHA-256 identities, human decisions, and release manifests are preserved without claiming provider acceptance.

---

## 2. Recent Completed Slices (Handoff Summary)

### 2.0 MudBlazor UI Content Migration (commit `e66bc49`)

- **All inventoried routes migrated:** every AuditSphereOps route renders its content
  through MudBlazor 9.10.0 primitives (`PageHeader`, `MudPaper`, `MudAlert`, `MudTable`,
  `MudButton`, `MudLink`, `StatusChip`, `LoadingState`, `MudGrid`) inside the existing
  Blazor Interactive Server boundaries. No authorization, revision-fencing or
  persistence behaviour changed.
- **Contracts preserved:** native `h1`/`h2`, `id`, `aria-labelledby`, `role`,
  `data-draft-*`, form `id`s and `.command-result` status text are retained;
  `draft-state.js` and `pbc-upload.js` are byte-identical to `master`.
- **Two E2E regressions found and fixed in the UI layer:** the contiguous
  `Status: SENT` invoice text, and the `GetByRole(AriaRole.Region)` lookup of the
  time form (labelled cards now carry an explicit `role="region"`).
- **Documented exceptions** in
  [retired UI migration history](https://github.com/nirzaf/AuditSphere/commits/master/docs/auditsphere-ui-mudblazor-conventions-migration-current.md):
  6 native tables (`tfoot`/`colspan`) and the native browser-draft boundary
  elements. `/app/accounting/remeasurement` was left at its master markup in
  this slice because its browser-draft reload journey was sensitive to the
  MudBlazor render path; slice 3 migrated it (see 2.1).
- **State:** committed and pushed as `e66bc49`. No tenant operation or
  production effect.

### 2.1 MudBlazor Form Controls, Navigation & CSS (slice 3)

- **Lockfile health fixed:** the MudBlazor commit left
  `tests/AuditSphereOps.Api.Tests/packages.lock.json` and
  `tests/AuditSphereOps.E2E.Tests/packages.lock.json` stale (`NU1004` in locked
  mode). Both were regenerated with `dotnet restore --force-evaluate` on the
  repository-pinned SDK; locked-mode restore now passes with 0 errors and no
  unrelated package changed.
- **`CurrencyRemeasurement.razor` migrated:** MudBlazor shell (`PageHeader`,
  `MudLink`, `LoadingState`, `MudAlert`, `MudPaper`, `MudTable`, `MudButton`,
  `StatusChip`) with `MudTextField`/`MudNumericField` line fields whose inner
  inputs carry the forwarded `data-draft-field` attributes. The draft boundary
  section, its five selects and the date input stay native because
  `draft-state.js` reads GUID/bool/ISO values from `control.value` and the E2E
  journey asserts those exact values after reload. The draft-restore journey
  `CurrencyRemeasurementWorkbenchRestrictsContextAndRestoresBrowserDraft` passed
  9/9 explicit runs plus the full-suite runs (previously flaky).
- **Form controls migrated on 15 pages** with typed components
  (`MudTextField`, `MudNumericField<T>`, `MudSelect`, `MudCheckBox`); no native
  `<button>` remains anywhere. Two selects reverted back to native after the
  full suite exposed hard contracts (Playwright `SelectOptionAsync` on the
  package-review Stage select; `Locator("#decision-outcome")` visibility on the
  assessment decision page; raw-value `InputValueAsync` on the M365 capability
  selects) — documented in the migration document and in inline comments.
- **Accounting navigation standardized:** `AccountingNavigation.razor` and the
  `Consolidation`/`AccountingRecords` page tab rows now use a `MudPaper` bar of
  `aria-current`-carrying links (`.accounting-nav`); the broad `/app/accounting`
  prefix bug is fixed so only the correct entry is active.
- **CSS consolidated:** `.accounting-tabs`, `.button`, `.card-grid` and
  `button:disabled` removed; `.accounting-nav` rules added; no second CSS
  component framework. Shared components reviewed; MudBlazor remains Web-only
  (`ArchitectureGuardTests`).
- **State:** full Release suite green (counts in `status.json`), no EF model
  drift, no schema migration, tenant operation or production effect.

### 2.2 MudBlazor Migration Completion (slice 4)

- **Last two select exceptions closed at full assertion strength:** the
  `FinancialPackage` Stage select is `MudSelect`, driven by a new
  `SelectMudOptionAsync` journey helper (open the labelled select, click the
  exact option) choosing the same value with unchanged downstream assertions;
  the `AssessmentDecision` `#decision-outcome` id moved to the visible MudSelect
  wrapper `div`, so the partner visibility and unauthorized-manager absence
  checks needed no test change. The Microsoft 365 capability selects stay
  native permanently (raw-value `InputValueAsync` contract).
- **`draft-state.js` guard:** `<input type="hidden">` composite-widget internals
  (MudSelect combobox mirrors) are excluded from draft discovery and
  collection; visible draft-field semantics unchanged; `pbc-upload.js`
  byte-identical.
- **Route-render guarantee:** `RouteRenderSmokeTests` visits every
  parameterless inventoried route (19 staff routes plus the client portal)
  asserting heading render with zero page errors; detail routes keep their
  seeded journeys.
- **State:** Domain 338/338, Api 6/6, E2E 61/61, no EF model drift (counts in
  `status.json`); commit `dc43bc0` pushed; no tenant operation or production
  effect.

### 2.3 MudBlazor Migration Final Reconciliation (slice 5)

- **Verified counts recorded:** 42 page components declaring 49 routes,
  zero native `<button>`, 39 intentional native controls across 9 pages, and
  5 native tables (previously recorded as "47 routes / 6 tables" — corrected).
  See the retired MudBlazor conventions document in Git history, §8.
- **Last cleanup:** StatusChip now maps `POSTED`/`RECONCILED`/`ACCEPTED` to
  success and `RESUBMITTED` to info; the dead `.field-label`/`.context-bar`
  CSS rules were removed. `MainLayout`, `AuditSphereTheme`, `PageHeader`,
  `LoadingState`, `ScopeBanner` and `ConfirmDialog` reviewed and unchanged.
- **State:** Domain 338/338, Api 6/6, E2E 61/61, no EF model drift; commit
  `3d449fb` pushed; no tenant operation or production effect.

### 2.6 Stale-Route Audit Completion (engagement detail fix + four journeys)

- **Real leak fixed:** `EngagementDetail` loaded only during initialization, so a
  same-document route change to another engagement kept the previous engagement's
  details, client name and holds on screen without reauthorization. Loading now
  runs per parameter set with a per-parameter guard and clear step; an
  unauthorized engagement renders "Engagement unavailable" with all prior
  markers cleared.
- **Four new same-document journeys** record the reauthorization contract for the
  remaining detail screens (engagement detail, audit plan materiality, audit
  population, release candidate) using the established authorized-read →
  out-of-scope pushState → cleared-projection → return → document-token pattern.
- **Observed:** the four focused cases pass 4/4; the complete suite passes
  Domain 344/344, Api 7/7, E2E 79/79 with 0 skipped on the shared tree. Commit
  `81d4c1b` pushed to `master`. The parallel session's in-flight PBC work was
  left untouched and excluded from this commit.

### 2.7 Documentation Health Gate (CI)

- **Automated documentation health wired into CI:** a new `docs-health` job in
  `ci.yml` runs the Markdown health validator (canonical links, basenames,
  HISTORICAL_SOURCE banners) and the filename-policy validator on every push
  and pull request — pure stdlib Python, no .NET or database needed, completing
  the first "Automated Documentation Health" item from the pending-work
  inventory. The build job is unchanged; hosted test execution remains the
  retained blueprint.
- **Wording synced:** the README and testing-strategy CI-contract sentences now
  describe the gate as build plus documentation health checks; the pending-work
  inventory marks the item complete with the volatile-metrics guard remaining.
- **Observed:** both validators pass locally on the changed tree; the workflow
  parses with both jobs; no .NET suite rerun (workflow + docs change only).

### 2.8 Workpaper Revocation-Refresh (AS-PAR-002)

- **Reload affordance added:** the workpaper draft toolbar's "Reload current
  target" button was previously rendered only during draft conflicts; it is now
  always available, so a user (or the audit) can re-run the reauthorizing load at
  any time. `ReloadCurrentTargetAsync` already clears the protected projection
  and re-checks scope and grants on every use.
- **New journey:** `ReloadCurrentTargetClearsWorkpaperAfterGrantRevocation` —
  opens an authorized workpaper, revokes the Partner and Staff grants via
  `RoleAdministrationService.RevokeRoleGrantAsync`, clicks "Reload current
  target", and asserts the fail-closed "Workpaper unavailable" state with the
  title, index and work-performed markers fully cleared.
- **Observed:** the focused journey passes; the complete suite passes Domain
  348/348, Api 7/7, E2E 80/80 with 0 skipped on the shared tree. No business
  rule, authorization decision or persistence change; the reload path simply
  makes the existing fail-closed reauthorization reachable at any time.

### 2.9 Revocation-Refresh Completion (finding, audit plan, invoice detail)

- **Refresh affordances added to the last three screens:** "Refresh finding"
  (`RefreshFindingAsync`: clear + re-run the scope-and-grant-authorizing load),
  "Refresh plan" (`RefreshPlanAsync`: generation-guarded clear + reload through
  the same path the route parameters use) and "Refresh invoice"
  (`RefreshInvoiceAsync`: generation-guarded clear + role re-resolution +
  scoped load). All three handlers re-resolve the actor and re-run the
  authorization decision, so a revoked user gets the fail-cleared denial state.
- **Three new journeys:** `FindingRefreshClearsAfterGrantRevocation`,
  `AuditPlanRefreshClearsAfterGrantRevocation` and
  `InvoiceRefreshClearsAfterGrantRevocation` follow the established
  revoke-while-open pattern (revoke via
  `RoleAdministrationService.RevokeRoleGrantAsync`, click refresh, assert the
  denial heading with every projection cleared, zero page errors).
- **Observed:** the three focused journeys pass 3/3; complete suite Domain
  348/348, Api 7/7, E2E 82/83 on the shared tree — the single failure
  (`FinanceManagerScopedToAnotherClientCannotViewInvoiceOrFallbackBalance`)
  is the documented intermittent-under-load pattern and passes in isolation
  and together with both new invoice journeys. No business rule, authorization
  decision or persistence change.

### 2.10 Benchmark Baseline Verification

- **Provisional maximum-input samples:** The owner selected the documented
  input sizes for local measurements. New PostgreSQL-backed cases import a
  balanced 20,000-row trial balance through the guarded service and transfer
  one 8 MiB PBC chunk through the durable worker and simulation sink. Each
  case verifies final state, exact identity/digest or row count, and duplicate
  or single-attempt behavior. Both cases passed in three isolated runs; the
  full Release solution suite, Web build and EF model check passed. Timings and
  exact checks are in `status.json`. These samples do not establish concurrent
  workload capacity, live provider behavior, p95 latency or production sizing.
- **Provisional concurrent query sample:** Fifteen explicit local users issued
  thirty simultaneous first-page GL queries through separate PostgreSQL-backed
  contexts. Three isolated runs passed, and the full Release solution suite,
  Web build and EF model check passed. Exact measurements are in `status.json`.
  This measures application queries rather than browser sessions or sustained
  HTTP load; it does not close P8b.
- **Explicit focused run recorded:** AccountingBenchmarkTests passed on the
  local PostgreSQL profile, covering intake, durable outbox enqueue/worker
  publication and consolidated group readback. Concurrent worker-operation
  coverage remains in DurableOutboxTests. Exact measurements and the observed
  date are recorded in `status.json`.
- **State:** documentation/ledger change only; no production acceptance
  claimed for local-loopback measurements.

### 2.11 Acceptance-Criteria Reconciliation Phase 1 (VP-034)

- **New Domain test:** `ChartValidation_RejectsDuplicateCodesCyclesPostingParentsAndInvalidDateRanges`
  proves the chart/period guards named by VP-034-AC02: inverted period date
  ranges, duplicate account codes (within a batch and across batches), a
  posting account used as a chart parent, parent cycles, and published-chart
  immutability under mutation (`ProtectedState`).
- **Coverage ledger reconciled:** VP-034-AC01 and VP-034-AC02 moved to PASS
  with named test evidence (`ClientChartsPeriodsAndGlImport_AreTypedScopedAndClosedSafely`
  for the context-binding and sibling-scope denial); VP-034-AC03 records its
  partially evidenced legs and stays NOT_RUN pending a package-staleness case;
  VP-034-AC04 remains NOT_RUN (UI-level verification). 47 NOT_RUN rows remain.
- **Observed:** focused test passes; complete Domain suite 349/349 with 0
  skips; task-pack validate 0 errors.

### 2.4 Documentation Reality Audit

- **Scope:** descriptive/current Markdown reconciled against implementation and
  the authority order; historical sources, evidence, tracking ledgers and
  generated task state untouched.
- **Corrections:** README (GROUP scope restored to the data-flow diagram,
  MudBlazor 9.10.0 Web-only row in the technology baseline, four missing
  routes in the workbench directory, solution-tree fixes); system
  specification §4.1/NET19 (MudBlazor pinned 9.10.0 recorded as implemented
  instead of "verify during bootstrap"); current-architecture doc (MudBlazor
  Web-only baseline bullet); testing strategy + catalog (discovery counts
  refreshed to the observed 420: Domain 341, API 7, E2E 72, dated, with the
  build-gate-only CI contract stated next to the retained pipeline blueprint).
- **State:** markdown health PASS (124 files, 2354 links), naming policy PASS,
  task pack 0 errors; no .NET suite rerun (documentation-only).

### 2.5 AS-PAR-002 Stale-Route Reauthorization (workpaper, finding, invoice detail)

- **Three new same-document journeys** extend the route-parameter
  reauthorization audit to the remaining audit/practice detail screens:
  `WorkpaperClearsPriorWorkpaperWhenRouteChangesInPlace` (sibling-engagement
  workpaper never leaks; "Workpaper unavailable" with all markers cleared),
  `FindingClearsPriorFindingWhenRouteChangesInPlace` (same contract for the
  finding detail) and `InvoiceDetailClearsPriorInvoiceWhenRouteChangesInPlace`
  (an unavailable invoice renders the fail-closed "Access unavailable" state —
  the page clears the resolved actor with the invoice — with the invoice
  number and line description fully cleared).
- **No UI code changed:** all three pages already reauthorize on parameter
  change; the journeys record the verified behavior with the identical
  document-token and zero-page-error assertions used by the existing cases.
- **Observed:** the three focused cases pass 3/3 repeatedly; complete E2E
  project 74/75 on a shared busy tree with the single failure being the
  previously documented intermittent AccountingPeriod case, which passes in
  isolation; Domain 341/341 and Api 7/7 on the same tree. Commit `aaa7bad`
  pushed to `master`. Work-tree changes from the parallel consolidation-guard
  session were left untouched and excluded from this commit.

### 2.1 Documentation Standardization & AI Navigability Refactor

- **Standardized Filenames:** All non-root Markdown files standardized to `auditsphere-<area>-<document-type>-<subject>[-<id>][-<status>].md` with globally unique basenames. Root `README.md` and `AGENTS.md` preserved as conventional exceptions.

- **Capability-Focused Partials:** Services (`ConsolidationService`, `ClientAccountingService`, `AccountingAnalysisService`, `AuditFieldworkService`, `FinancialStatementService`), `AuditSphereDbContext`, and `ClientAccountingTests` decomposed into focused capability partial files with unchanged static APIs and test semantics.

- **Architectural Reference Guards:** `ArchitectureGuardTests.cs` enforces project reference layering (Domain references no outer project, Application references Domain only, Infrastructure references Application and Domain, never a host).

- **Documentation Policy & Automated Guards:** Established `docs/architecture/auditsphere-architecture-document-naming-policy.md`, `scripts/docs/validate-markdown-filenames.py`, and `MarkdownNamingGuardTests.cs`.

- **R2R Source Hashing Preserved:** `HISTORICAL_SOURCE` banners added and excluded from hash verification, preserving exact SHA-256 requirement hashes for R2R modules 20–26 and Audit workflow sources.

### 2.2 Re-Review Repairs & Currency Translation Completion (RR-01–RR-09, F01–F08)

- **Stable Reserve Line Identity (RR-01):** Derived cumulative translation reserve line IDs from immutable translation snapshots (`TranslationIdentities.CumulativeTranslationReserveLineId`), making replay, approval, and readback stable.

- **Fail-Closed FX Rates (RR-02, RR-03):** Removed missing-rate aggregate fallbacks; missing rate purposes return typed `gate.blocked` errors. Same functional and presentation currency translates as identity rate 1 without consuming observations.

- **Exact Line Rate Identity (RR-04):** Manifest rows carry their own line's consumed rate purpose and rate; classification resolves through recognized declared sections or approved mappings.

- **IAS 21 Rate Bridge (F01–F03):** `LineTranslationCalculator` computes translation reserve from equity and P&L rate differences with zero residual signed sum (`GOLD-R2R-07`), persisting `CTA_RESERVE` in consolidation balances.

- **Task Scope Reconciliation (RR-08, F06):** Reconciled T054 as R2R-only handover, moving audit handover `AS-AUD-028-AC10` to T075 with declared audit acceptance inputs.

---

## 3. Observed Local Verification State

All verifications are run against local PostgreSQL 18.6 on port 5433:

| Check | Expected Result | Authority Pointer |

|---|---|---|

| **Solution Build (Release)** | `0 Warning(s), 0 Error(s)` | `dotnet build src/AuditSphereOps.Web/AuditSphereOps.Web.csproj --configuration Release` |

| **PostgreSQL Test Suites** | All tests pass, 0 skips (Domain, Api, E2E Playwright) | Consult [`docs/execution/status.json`](status.json) for authoritative counts |

| **EF Model Drift Check** | No pending model changes | `dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web` |

| **R2R Task Pack Status** | `valid: true`, 0 errors, 75 tasks | `python3 docs/task_breakdown/tools/task_status.py validate` |

| **Task Pack Self-Tests** | 21/21 passed | `python3 docs/task_breakdown/tools/test_task_status.py` |

| **Markdown Filename Guard** | 100% compliant, 0 collisions | `python3 scripts/docs/validate-markdown-filenames.py` |

| **Git Diff Cleanliness** | 0 whitespace or formatting errors | `git diff --check` |

---

## 4. Known Blockers & External Boundaries

The following external gates require live cloud infrastructure or partner sign-off and cannot be closed in local development:

- **P1 (Entra OIDC Authentication):** `BLOCKED_EXTERNAL` — requires live Azure AD tenant app registration and client secret mount.

- **P2 (Selected-Resource SharePoint/Graph):** `BLOCKED_EXTERNAL` — requires live Microsoft 365 tenant with selected-resource application scopes.

- **P3 (External Release Checkpoint Store):** `BLOCKED_EXTERNAL` — requires remote cryptographic immutable storage.

- **P7 (Cross-Store Recovery):** `BLOCKED_EXTERNAL` — requires multi-region cloud backup infrastructure and tested RPO/RTO.

- **P8 (Production Secrets & Observability):** `BLOCKED_EXTERNAL` — requires production Key Vault and OpenTelemetry collector.

- **P9 (Independent Merge Review):** `BLOCKED_EXTERNAL` — requires partner sign-off.

- **P10 (Real-Tenant Acceptance §47):** `BLOCKED_EXTERNAL` — requires customer-authorized tenant execution.

---

## 5. What the Next Agent Should Know

1. **Read Order:** Always read [`AGENTS.md`](../../AGENTS.md) and [`docs/auditsphere-docs-index.md`](../auditsphere-docs-index.md) before performing work.

2. **Architecture Authority:** [`docs/architecture/auditsphere-architecture-current-architecture.md`](../architecture/auditsphere-architecture-current-architecture.md) defines the implemented architecture. Do not introduce MediatR, microservices, or new layers.

3. **Capability File Map:** Consult [`docs/architecture/auditsphere-architecture-code-map.md`](../architecture/auditsphere-architecture-code-map.md) to locate relevant partial classes, domain records, and test fixtures.

4. **Volatile Facts Rule:** Never add live test counts, migration counts, or commit hashes to narrative documentation. Record them only in [`docs/execution/status.json`](status.json).

5. **Next Work Packages:** Consult [`docs/execution/auditsphere-execution-pending-tasks.md`](auditsphere-execution-pending-tasks.md) for open local work:

   - Continue the AC-01–AC-28 and AS-PAR-002 route/parameter revocation review.

   - Formalize G16 period-end open-item methodology approval.

   - Maintain documentation health validation.

---

## 6. Standard Verification Commands

```bash



# 1. Build solution in Release



dotnet build src/AuditSphereOps.Web/AuditSphereOps.Web.csproj --no-restore --configuration Release







# 2. Check for EF Core model drift



dotnet ef migrations has-pending-model-changes --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web







# 3. Validate R2R task pack & pinned source hashes



python3 docs/task_breakdown/tools/task_status.py validate







# 4. Run task status self-tests



python3 docs/task_breakdown/tools/test_task_status.py







# 5. Validate markdown naming policy



python3 scripts/docs/validate-markdown-filenames.py







# 6. Run domain and architecture guard tests



dotnet test tests/AuditSphereOps.Domain.Tests/AuditSphereOps.Domain.Tests.csproj --filter "FullyQualifiedName~ArchitectureGuardTests|FullyQualifiedName~MarkdownNamingGuardTests"



```

### Angular client and engagement overview extension

Added Application workspace projections, authorized HTTP contracts, lazy Angular detail routes and session-fenced reads. Engagement-only grants remain unable to read client-wide profiles or sibling engagements. Mutation panels remain pending migration; this is not full US-015 acceptance. Latest observed verification is recorded in status.json.

Angular commercial extension: proposal detail, independent commercial review, sent-state recording, client response, prospect conversion and reviewed proposal revisions now compose existing Application services. Full Angular migration and built-in-browser acceptance remain pending; observed checks are recorded in status.json.

Angular commercial intake extension: qualified leads now have bounded opportunity detail, reviewed discovery creation with a stable request identity, and initial proposal creation. Concurrent same-request creation and cross-firm read isolation were checked using PostgreSQL. Pricing/settings/documents/fee agreements remain pending; focused observed checks are in status.json.

Angular quotation extension: approved-rate hours, backend preview, versioned breakdowns and independent matrix approvals compose existing Application workflows. Reviewed rates/revisions are fenced under the existing firm lock; four-decimal factor validation aligns with the canonical idempotency hash. The targeted Playwright pricing/save/approval journey passed after its deferred-load wait was corrected. Full migration and built-in-browser acceptance remain pending; observed checks live in status.json.

Angular commercial document extension: generation and immutable artifact history/downloads are now available through the opt-in proposal route. Commands check the reviewed quotation and profile under the existing firm lock; letter generation retains professional acceptance, Partner and signature/seal guards. Focused PostgreSQL, API and Playwright checks passed, including the portfolio revocation regression after its search selector was scoped. Settings, fee agreements and complete migration/browser acceptance remain pending. See status.json for observed verification facts.

Angular fee-agreement extension: persisted milestone amounts, engagement linkage, finance-controlled invoice drafts and manual advance-payment commands now compose existing billing services from the proposal route. Concurrent links cannot overwrite; payment-reference replay rejects changed payment details. The commercial browser fixture needed a valid primary-contact email, preserving the existing conversion gate. Focused local checks passed; commercial settings, full migration and built-in-browser acceptance remain pending. See status.json for observed checks.

Angular commercial settings extension: versioned firm profile and confirmed matrix changes/deactivation now have opt-in Angular controls with server-side Administrator/Partner edit authority and reviewed-state fences. Focused unit, PostgreSQL, API and Playwright checks passed; existing document profile identities were retained, and EF reported no model drift. Full source parity and the wider migration remain pending. See status.json for observed facts.

Angular accounting read extension: opt-in Angular client search and profile/reporting-period views now compose bounded Application queries. Explicit client-wide authorization is required for client books; engagement-only grants are not widened. Angular build and contract tests passed. Setup mutations, period workbenches and accounting browser/security acceptance remain pending; observed verification is recorded in status.json.

Angular accounting profile extension: reviewed profile creation/revision now composes existing Application commands. Writes are serialized under the existing firm lock, revisions remain exact strings and unknown outcomes require a persisted-state refresh. Local builds and Angular tests passed; PostgreSQL/API/browser verification and the wider accounting migration remain pending. See status.json.

Accounting profile verification extension: PostgreSQL checks confirmed client/sibling isolation, engagement-only denial, stale-session refusal and one winning concurrent reviewed revision. The authenticated API contract check also covered accounting read denials and a profile write without CSRF. Built-in browser acceptance and remaining accounting workflows are pending; exact observed results live in status.json.

Angular reporting-period creation extension: explicit review, ISO date contracts and existing client-scoped creation now have Angular controls. Concurrent duplicate creation is serialized; PostgreSQL checks confirmed one period and refusal of a foreign prior-period link. Opening-balance evidence is not fabricated by setup. Builds and Angular contract checks passed; book setup, lifecycle workbenches and browser acceptance remain pending. See status.json.

Angular reporting-book extension: bounded book metadata and reviewed creation now compose the existing Application service. The selected period is locked before closed-state/duplicate checks. Focused PostgreSQL verification covered concurrent duplicates and closed-period refusal; local builds and Angular checks passed. Remaining accounting workbenches and built-in browser acceptance are pending. See status.json.

Angular accounting journey: synthetic PostgreSQL-backed Playwright setup created a reviewed profile, reporting period and reporting book, then verified persisted reload. The initial selector-label failure was corrected with an explicit period label; the rerun passed without page errors. This does not replace built-in browser acceptance or complete accounting parity. See status.json.

Accounting lifecycle backend extension: reviewed close/reopen contracts bind the client and exact period revision under the existing period lock. Readiness rechecks authorization; source close gates and reopen amendment evidence remain authoritative. Focused PostgreSQL checks and Release build passed. Angular lifecycle controls and browser acceptance remain pending; see status.json.

Angular period lifecycle UI extension: close readiness and decision authority are shown separately; closing/reopening requires explicit review and a reason. The synthetic PostgreSQL-backed Playwright journey passed through setup, close and reopen and verified amendment evidence. Builds and Angular contract tests passed. Built-in browser acceptance and remaining accounting workflows are pending; see status.json.

Angular amendment-history extension: authorized bounded reopen history now shows exact previous/current revisions, reason, actor identity and UTC timestamp. PostgreSQL projection and synthetic Playwright lineage assertions passed alongside builds and Angular checks. Roll-forward, restatements and full migration/browser acceptance remain pending; observed results live in status.json.

Angular roll-forward extension: reviewed external-evidence setup now sends exact monetary/date strings and the closed prior-period revision to the existing Application workflow. PostgreSQL checks preserved draft book copy, unapproved opening evidence and duplicate refusal, and verified stale revision denial. Builds and Angular checks passed. Validated source-package selection, bridge history/review and browser acceptance remain pending. See status.json.

Angular opening-balance extension: scoped bounded bridge history now displays exact amounts, residual, source hash, evidence and approval metadata. Reviewed approval locks the bridge and verifies client/source identity with current reviewer authority. Focused PostgreSQL checks passed along with builds and Angular contracts. Validated source-package selection and browser approval/roll-forward journeys remain pending; see status.json.

Accounting roll-forward browser extension: the synthetic Playwright journey now passes exact decimal opening amounts, closed-period roll-forward, draft book copy and reviewed bridge approval with persisted approver identity, followed by reopen and amendment history. This is automated local evidence; validated source-package selection and full migration/built-in browser acceptance remain pending. See status.json.

Angular source-package selector extension: bounded matching validated packages can now be loaded for the exact closed client period/currency. Selection supplies the persisted hash and retains the reviewed prior revision fence. Local builds, Angular tests and focused PostgreSQL/Playwright checks passed for empty matching sources, wrong-client refusal and external-evidence roll-forward. Positive package selection and full migration/built-in browser acceptance remain pending; see status.json.

Positive roll-forward source coverage: PostgreSQL checks selected the exact validated package, excluded review-required and other period/currency/client packages, refused a changed source hash and verified the persisted bridge source identity. Angular source decoding now has direct revision/currency tests. An invalid fixture status/date was corrected to the real catalogue. Production UI build and focused checks passed; positive browser selection and full migration acceptance remain pending. See status.json.

Positive Angular source-package journey: the existing local Application package fixture produced a validated package, which the synthetic Playwright journey selected for roll-forward and verified by exact persisted bridge identity/hash after reviewed approval. The journey passed; full accounting parity/migration and built-in browser acceptance remain pending. See status.json.

Angular chart read extension: scoped bounded revision metadata and deferred account hierarchy reads now compose Application queries. Account paging executes in PostgreSQL rather than materializing the entire chart; exact-client linkage, page bounds and final current authorization are checked. Builds, Angular contracts, PostgreSQL hierarchy assertions and the synthetic accounting Playwright journey passed. Chart mutation migration and full migration/built-in browser acceptance remain pending; see status.json.

Angular chart draft-write extension: reviewed creation and account entry now compose existing locked Application commands with latest/chart-version fences and exact client linkage. Published charts remain immutable. Builds, Angular checks, focused PostgreSQL locking/scope tests and the synthetic accounting browser journey passed. Publication, aliases/dimensions and full migration/built-in browser acceptance remain pending; see status.json.

Angular chart publication extension: an independent reviewer confirms a bounded full account-set digest; the existing locked command checks client/version/digest and preserves hierarchy validation and preparer separation. Builds, Angular contracts, PostgreSQL stale-snapshot/locking assertions and the synthetic reviewer Playwright journey passed with persisted publisher identity. Aliases/dimensions and full migration/built-in browser acceptance remain pending; see status.json.

Chart alias publication fence: the reviewed chart digest now includes source-account aliases. Alias writes accept exact client/version fences under the chart lock. The focused PostgreSQL workspace test passed, including wrong-client refusal and stale publication after alias changes. Alias UI, dimensions, full migration and built-in browser acceptance remain pending; see status.json.

## Angular client and engagement workspace administration

Added bounded administrator workspace pages and exact target/resource/template reviews to the standalone API and Angular tenant screen. Provisioning requires a reason, current authority and the reviewed fingerprint; publication is refused after configuration or session changes. Client folder readiness, disposable PBC verification and dedicated client-site membership remain separate states. The latter is read-only and retains the explicit whole-site Full Control warning and isolated privileged worker boundary. Worker receipts bind their original provider plan and refuse folder ownership collisions; reconciliation remains get-or-create. No permission or credential boundary was widened.

Verification results and the remaining migration gates are recorded in `status.json` after observation. This slice does not claim final Blazor retirement or live Microsoft acceptance.

## Native Angular confirmation lifecycle

The confirmation register and exact case-evidence reviews now have a dedicated Angular route, linked from fieldwork and completion. Local commands preserve scope, current session, reviewed revisions, independent review and frozen-file gates. Preparation never means delivery; only observed dispatch evidence starts follow-up monitoring. Current response and alternative revisions remain separate. Closure rejects nonresponse without reviewed alternative work, and late substantive responses require new independent review. Exact supported amount strings are preserved; unsupported storage precision fails validation rather than silently rounding. Unknown writes remain fenced until persisted refresh and fresh review.

Executed evidence and corrections are recorded in `status.json`. This slice does not establish complete migration acceptance, actual external dispatch or final Blazor retirement.

### Native confirmation batch preparation

The Angular confirmation workspace now prepares reviewed batches through the standalone API. Every case has an exact supported amount, source identity, respondent and validated contact source; area, currency, date and optional procedure are reviewed together. Any edit clears confirmation. Application serializes preparation with current scope/epoch/register checks and the frozen-file guard, rejects existing source/date identities and publishes all cases in one transaction. An invalid or duplicate case refuses the whole batch. Unknown outcomes fence repeat writes until persisted state is reviewed. These are local prepared cases; no Microsoft effect or confirmation dispatch is inferred. Closure-conclusion evidence is now retained as described below; full scoped draft restoration remains pending. Exact executed verification is recorded in `status.json`.

Responsive confirmation forms no longer impose an intrinsic minimum width on the mobile shell. The single-column grid and main content can shrink; wide registers retain contained horizontal scrolling. Expanded batch forms remain usable at narrow widths.

### Confirmation closure evidence retention

The source confirmation lifecycle now retains each new human closure decision with its actor, conclusion, timestamp and exact reviewed evidence snapshot. PostgreSQL refuses closure-row updates and deletion. Native Angular displays the retained conclusion and digest; historical closures with no record are labelled explicitly. Case locking and deterministic current-alternative ordering prevent stale or repeated closure decisions. The additive migration is exercised only in owned synthetic test databases; shared Development and production migration application remain operator-controlled. Scoped draft recovery and the broader migration acceptance remain open. Executed verification is recorded in `status.json`.

Confirmation draft recovery extension: case, batch and action forms now use versioned explicit tab drafts, exact identity/session/entity/current-base checks, expiration and bounded allowlists. Recovery clears review; pending submission intent requires persisted-state refresh. Navigation/context changes offer keep, save and continue, or discard, with honest memory-only fallback when storage fails. Background revision changes retain edits while blocking stale submission. This leaves the general draft primitive and remaining module/story acceptance open. Observed verification and browser evidence live in status.json.

## Native Angular journal revision lifecycle

Journal detail now provides exact source/context review, focused balanced-line editing, fresh
server preview and assent, submission/return, independent technical posting, linked reversal,
and read-only retained before/after evidence. Application owns the scoped serialized transaction,
final authority/period/freeze checks and immutable actor/request receipts. Unknown responses fence
new actions until receipt reconciliation; tab recovery excludes assent. Posted lines remain frozen,
including attempted moves to an editable journal. The existing returned-edit service and database
guard now agree. This does not apply a treatment to client books or a package.

Observed local checks and original failures are recorded in `status.json`. The additive migration
has been exercised in disposable test databases only. Native creation is described below;
management responses, reflection/application controls and wider migration acceptance remain pending. The prior frozen
mapping regression's historical-schema failure remains distinct from its verified fixture repair.

### Native journal creation from a sealed source

Source inspection now enters a dedicated Angular draft-creation page. Exact source/period/book
and purpose/origin are reviewed with all decimal lines, rationale and evidence. Application
serializes creation with the existing service and a retained original-line event. Identical
requests reconcile; duplicate numbers, changed prior-journal revisions and late authority loss
cannot publish another draft. Corrections can link an exact same-context posted/returned
journal while retaining it unchanged. Unknown responses require receipt reconciliation and
acknowledgment; tab fields never restore assent. The additive event migration is exercised only
in disposable databases. Executed checks are recorded in `status.json`; management responses,
reflection/application and full migration acceptance remain open.

### Native journal management responses

The owned Angular slice adds a bounded scoped client queue, exact management review and separate
staff offline evidence. Application derives evidence mode, fences exact current source/journal
context and commits the decision with its immutable actor-owned receipt. Database guards freeze
reviewed lines/context and retain client identity separately from offline staff recording.
Unknown acknowledgment recovery requires an explicit scoped receipt check and acknowledgment;
client fields remain in memory and staff tab recovery excludes assent. Technical posting and
source/package application remain separate. Final isolated backend, Angular, affected Domain/browser,
model-drift and built-in browser gates passed as recorded in `status.json`; wider migration remains open. The completed frozen journal
regression has terminal acceptance with unchanged source/artifacts. Creation regression remains
independent and failed on retained legacy browser timeouts; its corrected affected cohort is a
separate pass. Overlapping source-reflection edits were preserved outside this owned worktree.

### Native adjustment-plan eligibility review

The next owned slice adds a bounded native queue and read-only plan review. It keeps retained
calculation evidence separate from current applicability and reports the exact planned/current
source-reflection states. The existing eligibility report now agrees with finalization when
reflection changes or disappears and rejects ambiguous, group-only or oversized membership.
Scoped source parents, current authority and supporting-context double reads protect results.
The initial build and queue/fixture failures were corrected before final acceptance. The
queue filters held engagements and preserves per-plan professional checks. Targeted final-source
backend, Angular, affected Domain/browser, model and built-in browser checks passed. The full
API regression completed with unchanged frozen source/artifacts. Observed counts,
failures, artifact fingerprints and publication are recorded only in `status.json`.
Native plan creation/finalization is described below; wider migration acceptance remains pending.

### Native reviewed adjustment-plan creation and finalization

Exact source inspection now enters bounded posted-revision selection and a fresh server preview.
Creation retains reviewed membership and actor-owned request evidence together. Finalization
validates exact accounts/context/reflection, previews six-decimal totals and a canonical hash,
then preserves the calculation and immutable event atomically. Source books and package release
remain separate. Database guards refuse retained membership/context changes and evidence deletion.
Native legacy-finalize bypass is refused through a typed Application result. Tab fields exclude
assent; unknown outcomes require persisted receipt verification and acknowledgment.

Angular, focused PostgreSQL API, affected Domain, model and built-in browser checks passed.
The corrected final affected browser cohort and retained-evidence rollback/reapply checks passed.
The broader final API regression remains active; its prior implementation pass is recorded
separately. The migration was exercised only in disposable owned databases. Exact terminal
results, initial failures and unchanged artifact fingerprints are recorded in `status.json`.

### Native administration access operations and cross-module parity audit (ZCode lane)

While the parallel Codex session continued the accounting chain, six read-only
source-action parity audits covered the Codex-disjoint modules: practice/finance,
administration/Microsoft365, audit/library, assurance/completion, commercial/CRM
and operations/records/consolidation/portal. Verdict: zero missing user-invokable
commands anywhere — every retired-host action already has a native component
method, same-origin endpoint and Application service, and several native flows
(digest-verified access review, quotation concurrency, preparer/reviewer
segregation) exceed the retired host.

The owned slice restored the administration capabilities that existed only in
Application: reviewed assignment now optionally records the atomic copy-link
invitation intent (the preview digest is re-verified server-side against a fresh
preview before ApplyRoleGrantAndInvitationAsync), invitations are retrieved for
copying through a scope-rechecked read and copying is recorded, an
approved-roster/verified-sign-in identity binding fallback covers deployments
without a directory reader, and the access workspace now carries per-grant change
evidence plus each user's copyable invitation id.

Smaller parity repairs landed with it: legacy assessment deep links accept
EngagementLeader and Auditor; the partner decision flow differentiates stale
generation (form cleared, re-review required) from denied scope and prefills the
service route; the finding response form prefills the recorded response; the
workpaper autosave debounce matches the retired host; the invoice footer totals
the rendered lines with exact-decimal arithmetic; the portal setup-pending panel
only renders with zero authorized engagements; the Angular-mode access-denied
page is a friendly no-store HTML page with a sign-out action instead of raw JSON;
and the practice time, firm books and lead creation forms persist identity-scoped
field drafts with allowlist validation that never stores assent.

Remaining from the audits (recorded in status.json): administration pre-dispatch
form recovery and setup capability-state fields, commercial proposal read-model
enrichment and wider form draft hooks, receipts/credit notes (Application-only in
both hosts, parity-neutral), curated parity ledger files for the audited modules,
and the whole-migration acceptance gates that stay owner-gated.

### Proposal read-model parity and performance budgets

The commercial lane continues without touching the parallel assessment work.
The proposal workspace restores every read-model fact the retired host showed
(commercial owner, author, reviewer, approval/delivery/response timestamps,
supersedes lineage and per-revision sent/response columns) through one bounded
name lookup, and the leads list regains its summary metrics, recorded dates and
refresh control. The proposal decoder contract was extended at equal strength.

US-043 now has a local production-like budget journey: cold signed-in first
paint, warm lazy-route load, client-side router navigation and a 60-lead
dataset render against deliberately loose ceilings. The observed run recorded
1500ms cold, 32ms warm, 66ms router and 96ms large-dataset, all inside budget;
exact values live only in status.json.

The Angular conventions and safe AI-assisted implementation document is now
CURRENT under docs/architecture, and the curated cross-module parity ledger
(28 retired-host sources, 25 curated source-action verdicts) landed under
docs/execution as the US-044 record for the audited non-accounting modules.

### All-module accessibility sweep

The US-042 lane gains a pinned sweep journey: one staff identity walks a
representative route from every native module and each rendered state must
keep its document language, a single visible h1, named controls, labelled
fields, captioned tables and WCAG AA contrast against the effective
background, with an Escape-closable search overlay and a German-locale
context proving locale-dependent rendering. The first run surfaced exactly
one finding class - captionless tables on the practice time page, fixed for
both the staff list and the approver queue - and the sweep now passes across
all twenty routes with zero page errors. Real screen-reader walk-throughs and
a wider locale matrix remain open before the whole-migration acceptance gate.

### Deep accessibility and locale acceptance

Beyond the DOM sweep, the deep acceptance journey pins what assistive
technology and throttled readers depend on: landmark structure, a
non-skipping heading hierarchy, the skip link as the first tab stop handing
focus to main, live-region announcement of command outcomes, and the
administration dialog matrix - focus enters, Escape closes, focus returns to
the trigger, and history disclosures toggle an honest aria-expanded state.
The locale matrix signs in and renders under fr-FR and RTL ar-EG while staying
honestly LTR-declared, and a constrained-network budget loads a cold lazy
route at 4890ms observed against a 25s ceiling. A human screen-reader
walk-through and production acceptance remain the open gates.

### Reviewed native analytical preparation (US-025)

At code commit `d2fce44`, the engagement workflow now opens a native Angular analytical-preparation form. It loads only authorized reporting periods, fences the command to a reviewed period basis, accepts exact six-decimal practitioner inputs, previews the variance server-side, and requires explicit assent before retaining the analysis. A zero prior amount stays explicitly insufficient rather than inventing a ratio.

The Application command writes the existing analytical-review record and an immutable actor-owned idempotency receipt in one PostgreSQL transaction. The additive migration binds the receipt to the exact actor, period and analytical row, freezes preparation inputs after receipt, permits separate human-review fields to change, and refuses destructive rollback while receipts exist. The browser journey drops the successful response and confirms the saved receipt after reload without duplicating the analysis.

Focused verification and commit attribution are recorded in `status.json`. The migration ran only in owned synthetic PostgreSQL databases. The Development/production databases were not migrated, and the full solution suite was not rerun at this commit. US-025 remains partial: new reconciliation and specialist preparation/revision forms are still open, along with wider story acceptance and external migration gates.

### Client portal PBC upload recovery (US-013)

At code commit `64e83a3`, an interrupted client upload can be resumed after the client reselects the exact file. The authenticated API verifies current participant/write authority, filename, byte count and SHA-256, then rotates a one-transfer capability and returns the verified contiguous byte offset and next chunk index in a no-store response. Only the capability hash is persisted, and chunk authorization is rechecked after locking the upload row. The interrupted-acknowledgement browser journey confirmed the exact original bytes resume into two unique chunks with no duplicate upload intent. The normal client portal upload regression also passed.

Focused evidence and the 505.46 kB Angular initial-bundle warning are recorded in `status.json`. API contracts passed 2/2, the two portal browser journeys passed 2/2 on isolated PostgreSQL test data, Angular CI passed 475/475 on the shared checkout, and EF reported no pending model changes on the isolated source snapshot. The shared-tree API test setup encountered the other agent's uncommitted EF model drift; the same focused tests passed from the isolated snapshot. No Development or production database was changed. Overall upload parity, the full solution regression and production acceptance remain open.

### Blazor retirement source inventory and removal stop gate

The current source snapshot now has a reproducible discovery register for the legacy Web project, a route-ownership matrix, feature/test crosswalks, and a removal-readiness decision. The generated source rows remain `NOT_ANALYZED`; Angular route ownership is not treated as behavior parity. The production API rejects the legacy presentation outside `Test`, while the solution and API/E2E test fixtures still depend on the Web project. The removal recommendation is `NOT_READY`; no Web, Razor, or MudBlazor files were deleted. Current counts and focused verification are in `status.json`; see `docs/migration/auditsphere-migration-blazor-retirement-final-report.md` for the stop-gate report and remaining work.

### Operations retry response-loss reconciliation (AS-PAR-002)

At code commit `15834ec2`, the Angular Operations page reconciles a retry whose
POST was accepted but whose browser response was lost. A scoped refresh that
finds the exact operation in `RETRY_WAIT` clears the unknown-outcome guard and
reports confirmation from persisted state; no second POST is sent. Other
mutation controls stay disabled while the outcome is unresolved. A synthetic
PostgreSQL/API-host Playwright journey confirms one dispatch, one actor-attributed
append-only retry event, the persisted state, ordinary-staff denial, and no
horizontal overflow or clipping at 1141, 1024, 700 and 390px. The operations
journey plus the related firm-scope revocation cohort passed 2/2. Angular CI
passed 493/493 across 95 files and the production build passed with the existing
7.53 kB Commercial Settings stylesheet warning. The built-in Development
browser still denies operation data to the current staff identity and records
no console errors. Full solution regression and EF drift were not rerun; no
Application, API or EF model code changed. The source/action row and AS-PAR-002
remain partial, and Blazor retirement remains `NOT_READY`.

## Firm fee receivables ageing (AS-COMP-28)

At code commit `83a6c624`, the report query now includes only posted advance
and final invoices bound to a fee milestone whose agreement belongs to the
invoice's billing client. It fails closed if a returned invoice has an
ambiguous milestone binding, preventing unrelated posted billing from being
presented as collectible fee receivables. The accompanying regression fixtures
create real accepted-proposal fee agreements and also post a generic invoice;
the report and export journey proves the generic invoice is excluded.

The full slice adds evidence-backed, independently reviewed due-date revisions;
historical UTC as-of calculations for invoice postings, allocated receipts,
approved allocation reversals and issued credits; date-only ageing bands;
undated and settled states; client/engagement/currency subtotals; finance
follow-up contacts; a protected CSV export; and a native Angular finance route.
Receipt unapply is maker/checker reviewed and does not change a report balance
until approval. A pending or rejected terms change does not replace the last
approved due date. Unallocated cash is excluded, amounts in unlike currencies
are not combined, and there is no separate dispute status in the current billing
model, so outstanding disputed amounts remain aged until credited or reversed.

Verification on the pushed `master` source: the focused PostgreSQL aging suite
passed **10/10**, the API authorization/terms/reversal/export journey passed
**1/1**, Angular CI passed **502/502** across 96 files, the Angular production
build passed with the existing 7.53 kB Commercial Settings stylesheet budget
warning, the Release solution build passed with zero warnings/errors, and EF
reported no pending model changes. In the built-in browser the fresh route
served successfully and rendered the report header, date input, refresh action
and safe authorization state. The current browser identity lacks a firm-wide
FinanceManager/FinanceReviewer grant, so the report data remained denied and
CSV export stayed disabled; the authorized route behavior is covered by the API
journey. No business mutation or database migration was performed by the browser
check.

The serialized full solution regression was attempted but stopped after 15
minutes while the API test host was still active and before any project summary.
It is recorded as incomplete, with no whole-suite pass or totals claimed. The
exact commands and outcomes are in `docs/execution/status.json`.

## Firm expense creation outcome recovery (AS-PAR-002)

The existing implementation at `dbd8a375` was reverified on the current
`bcf9e912` checkout. `AngularFirmExpenseCreationRecoveryJourneyTests` passed
**1/1** in 36 seconds using the API-hosted Angular journey and an owned
PostgreSQL database. A response lost after a saved create was reconciled by the
exact actor/request ID/request hash receipt without a second POST. A request
that never reached the API remained unresolved until explicit retry with the
same form and file bytes. Duplicate identical intent returned the original
expense ID; changed intent conflicted; the database retained one row per
request. A reviewer without the preparer capability received a safe denial
without expense identity or payee data.

This closes the specific expense-record creation recovery gap in the finance
source review. Broader firm-books authoring is still incomplete: the native
ledger exposes account and period lists plus period close, while firm chart,
period, and manual journal authoring do not yet have a complete Angular/API
workflow. AS-COMP-26 remains open. The full solution regression was not rerun;
the latest complete result remains 1001/1001 at `ead85032`. The test compiled
the shared checkout alongside another agent's unrelated uncommitted engagement
lifecycle route, which the tested finance journey did not exercise.

## Currency-aware SRM differences and honest completion documents (AS-COMP-19)

Revalidating the owner's completion backlog against the current head closed several findings
by evidence first: the materiality policy (AS-COMP-12), balance-derived FSLI risk bands
(AS-COMP-13), attribute-strata sampling (AS-COMP-17), monthly firm P&L from period movement
(AS-COMP-27) and firm receivables ageing (AS-COMP-28) are all implemented at the current
revision. The remaining calculation-facing defect in the completion chain was the review
finding that the Summary Review Memorandum reports one signed sum of uncorrected differences
and raw serialized materiality.

At base `24eadbdb` (the two slice files landed on `master` inside the parallel session's
`68500767` engagement-lifecycle commit with byte-identical content), the Summary Review
Memorandum now presents uncorrected differences per currency with gross and signed-net
amounts and a factual SAD/PM comparison that is explicitly unavailable without
Partner-approved materiality or an approved translation basis. It adds a readable
PM/TE/SAD materiality section with currency and benchmark source, a Red-areas callout,
AJE proposed/agreed/rejected/applied counts, unresolved analytical-review variances, and
confirmation outstanding/critical/closed totals. No immateriality conclusion is emitted
anywhere in the memorandum. The Audit Findings Report replaces its single signed
cross-currency total with the same per-currency table, and the representation letter
replaces the unconditional computed immateriality assertion with the recorded per-currency
facts plus management's own signed opinion, so the professional judgment stays with the
human signatory. The facts digest now covers materiality currency, red areas, per-currency
difference summaries and unresolved variances: changed inputs stale dependent memoranda
while historical documents remain immutable.

Verification ran in an isolated worktree because the shared checkout carried the other
agent's in-flight planning/CRM edits. The fixture-eight journey proves equal-and-opposite
QAR differences render gross 20,000.00 with signed net 0.00 and "Gross exceeds PM.", a USD
difference stays separately identified with an explicitly unavailable comparison, the
memorandum contains no immateriality wording, and the preparer cannot set the correction
state while the Partner proposes one AJE and rejects another with a recorded reason. The
focused PostgreSQL test passed 1/1, the full Domain suite passed 692/692, the Api suite
passed 210/210, the whole-solution Release build passed with zero warnings/errors, and EF
reported no pending model changes. Repeating the deliverables class and the Release build
at the committed head `68500767` passed 7/7 and zero/zero; the shared checkout's failing
deliverables runs (5/7) are attributable to the other agent's uncommitted edits, since the
identical committed content passes in isolation. E2E/browser journeys and the serialized
whole-solution regression were not rerun for this slice: no API route, contract or Angular
surface changed. Tests used owned isolated PostgreSQL schemas; no Development or production
state was changed. Blazor retirement remains `NOT_READY`.

## Evidence-bound proposal dispatch and acceptance (AS-COMP-06)

Revalidating the completion backlog against the current head, the next P0 gap in the
commercial lane was the review finding that `SendProposalAsync` (and invoice send) meant
only a database transition. The invoice balance-payment side landed in the parallel
session's AS-COMP-08 work; this slice closes the proposal dispatch half.

The send is now refused without an exact recipient and queues exactly one durable email
(unique filtered index on firm and proposal) bound to the offer identity: the generated
quotation artifact hash when a reviewed document exists, otherwise a deterministic digest
of the reviewed proposal revision and its approved quotation. The commercial `SENT`
status no longer claims delivery — the truthful QUEUED/SENT/FAILED state with
`DeliveredAt` lives on the notification, is surfaced in the proposal workspace and the
Angular proposal page together with the recipient and offer identity, and the mail
worker's discovery now covers proposal dispatches without the fee-milestone join,
including pre-conversion sends with a null client scope that the operation store and
handler explicitly accept. Client acceptance must cite the exact dispatched offer hash
plus a respondent name: stale, revised or superseded offers are refused
(`crm.stale-offer`), replay of the same response is safe, and a response citing a
different offer conflicts instead of silently merging. The declined path keeps its
mandatory reason. The receipt notification binds its document hash and client for the
same discovery path.

Verification on the shared checkout: the focused dispatch journey passed 1/1 (refusal
without a recipient, exactly one queued dispatch on replay, stale-offer and missing
respondent refusals, evidence persistence, superseded-revision protection), the
commercial and CRM classes passed 24/24 including the two-mail worker delivery, the full
Domain suite passed 699/699, the Api suite passed 210/210, Angular CI passed 511/511
across 98 files, the Angular production build passed with the pre-existing 7.53 kB
settings stylesheet warning, the Release solution build passed with zero warnings and
errors, and EF reported no pending model changes. The focused and commercial tests were
re-run green at the final pushed head after the parallel session's portal edits landed.
Actual mail delivery and client-identity acceptance remain named live gates under
AS-COMP-31. The migration ran only in owned isolated PostgreSQL test schemas; no
Development or production state was changed. Blazor retirement remains `NOT_READY`.

## Blazor supporting-file review and current build check (2026-10-07)

The migration inventory had 17 supporting Web artifacts still marked
`NOT_ANALYZED`. I traced the rollback EF factory and project, actor adapter,
administration dashboard, theme/imports, host configuration, package lock, and
six browser assets to their current API/Angular/tooling owners. All 17 hashes
are pinned to `e9b10a75`; they now have evidence-linked `PARTIAL` reviews. The
11 supporting artifacts already reviewed in the shell/layout record remain
linked there. The generated inventory now shows all 28 supporting artifacts as
partial and zero as unanalyzed. No artifact was promoted to parity; exact
behavior, rollback and removal acceptance remain open.

The migration inventory generator passed both regeneration and `--check`; the
Razor source inventory check passed; status and review JSON parsed; and
`git diff --check` was clean. The optimized Angular build was retried from a
clean archive of current HEAD and the earlier `aca91b` source with Node 22.23.2
and two build workers. Both aborted with exit 134 and the esbuild
`all goroutines are asleep - deadlock` fatal error. Since the earlier source
reproduces it, this local result does not isolate a regression in current UI
code. No production bundle was emitted. Angular CI remains 556/556 from the
recorded run; the current built-in-browser attempt rendered only the signed-out
shell because the API host was unavailable. PostgreSQL, full solution and EF
checks were not rerun for this documentation review. The current migration
decision remains `NOT_READY`: 75 source/action rows are partial, and production
rollback/canary, human accessibility/locale, live Microsoft and separate owner
acceptance remain open.

## STE-REM-29 baseline repair and STE-REM-01 native client acceptance

Against the new pending-features review (baseline `7f080729`), the delivery sequence's
first item was restoring the exact-source validation baseline. The UI source-discovery
inventory regenerated with only the commit stamp and two Razor hash changes (76 pages,
no structural drift), and the committed OpenAPI artifact was regenerated with five
additive endpoints from landed commercial, milestone, portal-finance and statement work
(no removals). Both repository drift checks now pass locally and no check was weakened.

The review's most consequential finding — that Angular cannot record a client acceptance
because the native form predates the required offer-hash/respondent fields — is closed.
The proposal page's SENT state now shows the truthful dispatch evidence (queued for /
delivered to / failed for the recorded recipient, the dispatched offer hash, and the
reviewed fee, currency and revision) and its typed response form carries the respondent
name (required for acceptance), respondent email and evidence reference into the exact
backend fields introduced with evidence-bound dispatch. The stale "status only / no
email" copy was replaced with truthful mail-worker semantics. The proposal browser
journey exercises the real Angular → API → Application acceptance and decline paths and
asserts the persisted offer binding (`ResponseOfferSha256` equals the dispatched hash),
the respondent name and evidence reference, and the queued-dispatch display.

Focused verification: the proposal browser journey passed 2/2 (accepted and declined
variants), the CRM domain class passed 15/15, Angular CI passed 544/544 across 104
files (one unrelated gl-upload flake in a first run passed on immediate rerun), and the
Angular production build passed. The server-side acceptance validation, dispatch
binding and mail states were verified earlier and are reused unchanged; no Application
or Domain code changed in this slice, so the full Domain and Api suites were not rerun.
Live mail delivery and client-identity acceptance remain named live gates under
STE-REM-30/31. The browser journeys used owned isolated PostgreSQL schemas; no
Development or production state was changed. Blazor retirement remains `NOT_READY`.

## Handoff entries moved from the current slice on 2026-10-10

## STE v2.1 evidence mapping and full-suite runs — 2026-10-09

- Mapped 34 more journey steps to tests whose bodies execute the step: the manifest generator now cites 55 of 62 checks. Added `FieldworkOnSeparateFslisRunsInParallelForDifferentStaffWithoutInterference` (J23) and an explicit correspondence-trail assertion in the five-part bundle test (J40).
- Not cited, with reasons: J48 (no regulator or read-only export exists; the archive endpoint is a paged manifest read) and N02 (the services cannot record a Partner risk acceptance before commercial acceptance, and the letter gate checks commercial acceptance first). J12–J14, J47 and N13 stay `BLOCKED_EXTERNAL`.
- Full runs, recorded in `status.json`: E2E on `48168e2e` had one failure, an intermittent dialog-timing failure in a journey that passed in the three earlier full runs. Domain and API on `9013eeb2` failed only migration-rollback tests (1 and 10): rolling back from head runs the `PermanentFileFreeze` Down, which raises unconditionally. Because the cited suites recorded failures, the regenerated manifest reports 0 PASS; it is not inflated.
- Open owner decision: either keep the unconditional Down and rewrite or retire those rollback tests, or let the Down downgrade a database that holds no freeze or supplementary-record evidence (the convention the other evidence migrations follow). The change was drafted and reverted because it relaxes a compliance boundary the owner set deliberately.

## Terminal archive and mandatory advance control — 2026-10-09

Changes committed on 2026-10-09 after review of the v2.1 acceptance findings:

- Removed `AMENDMENT_OPEN`. Partner approval and closure now update a separate supplementary-record trail; neither changes the frozen state nor clears `ProfessionalWorkBlocked`. Angular and legacy Web screens say the archive remains read-only and disable editing when frozen.
- Added migration `20261009141822_PermanentFileFreeze`: legacy open amendments are normalized to `FROZEN` and the engagement write block is restored; the database check and trigger protect frozen identity/freeze evidence, and amendment approval/closure transitions remain auditable and immutable. Its Down migration fails closed. No database migration has been applied.
- Removed the configurable `WHEN_FEE_AGREEMENT_LINKED` exception. Activation always requires a linked fee agreement and paid advance; the review workspace reports the same blockers. ADR-0012 records the accepted mandatory rule.
- Added an EF design-time context factory with an inert local-only fallback so migration scaffolding does not construct the full API host. `AUDITSPHERE_MIGRATION_CONNECTION` is required to direct EF database commands at a real database.
- Lifecycle Stage 3 now requires a dispatched proposal; the projection separates local freeze and provider protection status. A due-but-uncommitted freeze remains in countdown with an explicit blocker.

Verification on the current source: the API Release build, focused PostgreSQL Domain cohort, updated archive Playwright journey, Angular production build and Angular CI specs passed. The Angular build retains the existing `settings.scss` budget warning. EF reports no pending model changes; OpenAPI drift verification and Markdown, filename, narrative-metric and source-inventory gates pass. Exact counts and commands are recorded in `status.json`. The built-in browser served the app and showed the expected unauthenticated sign-in gate; no authenticated lifecycle page was verified. The full E2E suite has not been run on this tree. External tenant and SharePoint archive acceptance remain `BLOCKED_EXTERNAL`.


## Pending-gap slice: fee-automation config alignment and truthful holding-letter delivery — 2026-10-09

Owner-directed attempt to close the tracked STE v2.1 gaps. Two locally-closable code defects were fixed and verified; the
remaining gaps are test-evidence, design or external work and were not started.

- **STE-GAP-002 (host config alignment).** `AutomaticFeeInvoices` was declared only in the worker's `appsettings.json`, but the API host reads the same key to report the advance-invoice preparation state. With the key absent it always reported "automation disabled", even when the worker had automation enabled. The section is now declared identically in `src/AuditSphereOps.Api/appsettings.json` and `src/AuditSphereOps.Web/appsettings.json`, and `FeeAgreementConfigurationTests.EveryHostDeclaresTheSameAutomaticFeeInvoicesPolicy` refuses a drifted value or a host that omits the section.
- **STE-GAP-005 (delivery truthfulness).** `HoldingLetterDispatch.StatusAsync` had an unreachable `FAILED` branch: nothing ever wrote `FAILED` to a `CommercialNotification`, so a blocked or dead-lettered mail operation was reported as `QUEUED` indefinitely, and the message told the operator to "dispatch again" although `QueueAsync` returns early for any existing notification. The status now consults the notification's durable operation and reports `FAILED` when that operation is in `OperationRecoveryService.RetryableStates`, naming the operations-screen recovery as the real route; `QueueAsync` states the no-op instead of implying a retry. `AuditDeliverablesTests` executes both branches. The Angular completion screen already renders the server state and message, so no UI change was required.

- **STE-GAP-010 (acceptance manifest evidence model).** `scripts/acceptance/generate-ste-manifest.py` had no evidence input at all: `check()` returned `NOT_EXECUTED` with an empty `evidence` list for every step, so the register could never produce a PASS and the "0 PASS / 57 NOT_EXECUTED" reading measured the generator, not the product. Each step can now cite `"<test source path>#<method>"`; the generator refuses to run when a cited file or method is absent, requires the owning suite to have a recorded run with zero failures, and carries that run's commit in the step's evidence so staleness stays visible. 21 of 62 checks now PASS (lifecycle, materiality and rounding, review notes, SRM and clearance, opinion and report, LOR, five-part bundle, freeze and early lock, archive read-only, plus negative branches N03/N06/N08/N09); the overall result stays **FAIL** because 36 checks have no mapped executed evidence and 5 remain externally blocked. A probe with a deliberately wrong method name was refused, confirming the guard.

Verification run in this slice: Release solution build 0 warnings / 0 errors; Domain `809/809` (3 m 3 s); Api `223/223` (16 m 50 s); `FeeAgreementConfigurationTests` `4/4`; `AuditDeliverablesTests` `12/12`; the manifest generator writes `21 PASS / 0 FAIL / 36 NOT_EXECUTED / 5 BLOCKED_EXTERNAL`.

**Not started — still open.** STE-GAP-001, 003, 006 and 009 need their refusal branches *executed* (the code paths exist); STE-GAP-008 needs the provider-protection state model and the stage-3 dispatch check; STE-GAP-010 needs the journey suites executed against the manifest checks and the manifest regenerated; STE-GAP-004 and STE-GAP-007 are `BLOCKED_EXTERNAL` and need a live tenant. The nine `BLOCKED_EXTERNAL` gates, AS-PAR-002/AS-PAR-009 and the `NOT_READY` Blazor retirement gate are unchanged. No `BLOCKED_EXTERNAL` gate was converted and no external effect was enabled.

## Requirements-alignment slice: advance gate and diagram traceability — 2026-10-09

Owner-directed alignment of the workflow architecture suite with the source requirements register (the source-aligned v2.1 register)
and with the code. Observed results:

- **Suite ↔ register links repaired.** All 59 baseline links pointed at the non-existent `requirements(2).html`; they were updated to resolve to the requirements register. All 22 distinct anchors (`#architecture`, `#boundaries`, `#controls`, `#lifecycle`, `#M1.01`…`#M5.02`) exist as `id` values in the register.
- **Persona cards** carry the register's §1.2 role names: Audit Associate / Junior Auditor, Audit Senior / Audit Manager, Engagement Partner, Client Coordinator / CFO / MD.
- **Profitability wording corrected** to match `PracticeAnalyticsQuery`: the source-defined charge-out indicator is stated, and the implemented actual-cost analytics (separate staff cost rate, profit, margin, realization, billed/collected) are described instead of being called undefined.
- **Three externally blocked steps marked `BLOCKED_EXTERNAL`** in the suite: live PBC portal provisioning and the first-sign-in evidence gate (ADR-0003: Microsoft sign-in, not emailed temporary passwords), the five standard engagement folders in the client SharePoint site, and provider-enforced archive immutability.
- **Advance gate is unconditional (ADR-0012, ACCEPTED).** `EngagementLifecycleService.ActivateAsync` refuses activation unless a linked fee agreement carries a `PAID` `ADVANCE` milestone. No configuration can weaken the mandatory control; the linked-only bypass was removed. `EngagementActivationWorkspace` reports the same rule as explicit blockers (`advance.fee-agreement-missing`, `advance.unpaid`) and includes the fee-agreement/advance position in the review-basis digest.
- **Configuration surface:** an `EngagementActivation` section was added to `src/AuditSphereOps.Api/appsettings.json` and `src/AuditSphereOps.Web/appsettings.json`; the options are registered in `ApiHost.ConfigureProviders`, so the API host and the legacy Web host compose them. No request or response contract changed.

Fixtures updated for the new default: `EngagementActivationReviewSeed.LinkFeeAgreement` stages lead → opportunity → accepted proposal → fee agreement → advance milestone; `AcceptanceChecklistTests` and the `OneClickDocuments…` commercial journey now link and pay the advance before activating; the new `EngagementActivationAdvanceGateTests` covers both modes and the unrecognised-mode refusal.

Verification run in this slice: Domain `809/809` (2 m 58 s) and Api `222/222` (17 m 7 s) against PostgreSQL `auditsphere_tests`; the two `AngularEngagementActivationJourneyTests` Playwright journeys `2/2`; Release solution build 0 warnings / 0 errors; `dotnet ef migrations has-pending-model-changes` reports no model change; `verify-openapi.sh` reports the contract current; the three Markdown documentation gates and `scripts/ui/inventory.py --check` pass (the Razor dependency row for `EngagementActivationPanel.razor` was regenerated).

**Not run:** the full Playwright E2E suite (only the activation journeys above), the Angular `test:ci` and production build (no Angular source changed), and any live external check. No production migration was applied and no `BLOCKED_EXTERNAL` gate changed state.

## Owner-delegated plan: T002, STE test gaps, STE-NXT-013 and stale-doc repairs — 2026-10-09

The owner delegated decisions on 2026-10-09 ("take any decision you wanted which makes the best for this project"). Observed results, in order:

- **T002 is COMPLETED** on reviewed commit `b24c8426`, through `task_status.py set`. The static-service variation is accepted; MediatR and bUnit are declined. The helper validated the checklist, the full SHA, the evidence file and the reviewer rule. T003 is now READY; the audit gives it an OWNER_DECISION verdict and it has not been started.
- **Contracts rewritten** for the variation in the affected task cards and module test tables. Request rows are unchanged, and the helper validates them.
- **Owner decisions recorded** (PROPOSED ADRs, awaiting owner confirmation): STE-NXT-010 Option A (ADR-0011); STE-NXT-009 option (b), a provision accrual entered by a person (ADR-0010), with follow-up STE-NXT-014. STE-NXT-011 stays open.
- **Test-only gaps closed:** STE-NXT-005 (successful Partner lock), STE-NXT-006 (NOT_APPLICABLE renders; the `AutomaticFeeInvoices:Enabled` key is tested), STE-NXT-004 (criterion figures and preparer refusal). STE-NXT-002 (ledger size budgets) and STE-NXT-007 (requirements-copy guard) are docs-gate checks; the guards were shown to fail on bad input.
- **STE-NXT-013 implemented:** the statement links send the keys the screens read; the analytical screen pre-fills only after the server confirms the statement revision, refuses a stale link, and offers a validated return; fieldwork focuses the risks for the line.
- **Stale statements repaired** against their records (telemetry hosts, restore-drill migration count, E2E checkpoint, Web parity row, agent-context hazards, helper test count).

Verification run in this slice: UI `test:ci` 113 files / 616 tests passed; the production build completed; Domain `PracticalRounding` 16/16; Api `FeeAgreementConfigurationTests` 3/3; `MarkdownNamingGuard` 2/2; the docs gates and `task_status.py validate` pass; the task board is current.

Not run in this slice: the full .NET suites and the Playwright E2E suite. The E2E run recorded in `docs/execution/status.json` (`testSuites.runs.e2e`) has 15 failing journeys at `f7304708`, which remain open.

Open for the owner: STE-NXT-011 (independent archive copy); confirmation of ADR-0010 and ADR-0011; the accounting treatment for end-of-service accruals before STE-NXT-014 posts anything; the 15 failing E2E journeys.

## Hostinger acceptance Microsoft 365 readiness snapshot (2026-10-10, refreshed)

The VPS checkout was fast-forwarded to master for the Compose mount; the existing API image was
retained (no new application image deployment). `docker compose up -d --no-deps --force-recreate
api` recreated only the API. Database and workers stayed up; no migration or Graph call occurred.
`/health/ready` returned HTTP 200.

To keep Selected Site inactive until the user's exact-site grant, `SELECTED_SITE_CLIENT_ID` was
cleared from the protected VPS `.env`. The certificate directory remains mounted read-only, but
the API has no app ID and reports the provider unconfigured. `ExternalEffects`, DirectoryReader,
tenant provisioning, guest invitation, group membership, outbound mail and live PBC transfer are
false.

The reloaded page shows consent `REQUIRED` with no attempt, directory `NOT_VERIFIED`, selected
resource `BLOCKED_EXTERNAL`, unavailable activation, and Mail/Records `Not Configured`. The
capability catalog's Selected SharePoint `Enabled` label is not evidence of a configured provider
or access; the runtime credential is absent. The user will grant only the exact site. Afterward,
restore the app ID privately and run the authorized positive and unrelated-site denial probe; never
add tenant-wide `Sites.FullControl.All` to the runtime app. User-reported Entra admin consent has
not produced an AuditSphere consent attempt or verified state. No site grant/probe or activation
was performed; M365 remains `BLOCKED_EXTERNAL`.

## Exchange authentication failure retained — 2026-10-11

The administrator completed the Microsoft device sign-in and its browser page showed success.
The Exchange PowerShell connection did not become usable: it reported `Broken` and token status
`Expired`. An earlier policy query returned object-not-found rather than verified mailbox
isolation. Repeated authentication attempts were stopped; no application-access policy or scope
group was created, no mail was enabled, and no synthetic message was sent. The mail credential and
approved sender remain staged privately on the VPS. Exact state is in [`status.json`](status.json).

Resume with a working supported Exchange administrator connection. Inspect the existing exact-app
policy, establish approved-sender access and unrelated synthetic mailbox denial, then enable only
the reviewed mail path and verify its synthetic message. A successful Microsoft browser page alone
does not prove a usable Exchange session or restricted mailbox access.

## Mail staging and repeated selected-site denial — 2026-10-11

The owner supplied the approved sender and confirmed the same controlled mailbox as the test
recipient. Exchange's mailbox inventory confirms it exists. The dedicated Mail registration's
existing public certificate matches the locally configured certificate; its existing credential
was transferred privately to the protected VPS mount. Only non-secret Mail settings were staged
in the protected environment, with Mail disabled and no API/worker recreation or message send.

Exchange Cloud Shell requires an unavailable Azure subscription. The installed local Exchange
PowerShell module offers a supported alternative; administrator device sign-in is pending before
mailbox restriction inspection. The exact certificate is reused, with no new Entra credential.

After the owner reported the exact SharePoint grant applied, a fresh certificate-backed token
again held only `Sites.Selected`, but the approved target still returned access denied; the
unrelated control was also denied. Repeated probes are paused pending grant-target investigation.
No site grant or file write was performed. Exact observations and CI evidence are in
[`status.json`](status.json). The previous documentation CI completed successfully.


## Sections moved from the current slice on 2026-10-11

The sections below were moved verbatim from `auditsphere-execution-current-slice.md` on 2026-10-11 to keep the active handoff under its size budget. They are historical.

## Task board, codebase audit and docs consolidation — 2026-10-08

A static audit of the 98 tracked task items was run at commit `13407156`: 74 R2R and AUD cards (T002 to T075), 12 STE-NXT stories and 10 STE-GAP stories; T001 is COMPLETED and was not re-audited. Each claimed COMPLETE was re-read by an independent reviewer. No item is COMPLETE: 70 PARTIAL, 13 CONFLICTS_WITH_AGENTS, 6 OWNER_DECISION, 5 EXTERNAL_BLOCKED and 2 NOT_STARTED. Six STE stories first claimed COMPLETE were downgraded (STE-NXT-002, 004, 005, 006, 007, 008). The audit read code and did not run tests.

The audit found that STE-NXT-008 was closed in error: the AR Test and Audit Workprogram links on the split dashboard pass context that the analytical preparation and fieldwork screens do not read. STE-NXT-013 is opened for it, and the gap tracker row 4.3.1 is now PARTIAL.

Consolidation: the four requirement documents moved to `docs/requirements/`, and every link and validator path was updated. The agent task board, [`docs/execution/auditsphere-execution-index-task-board-current.md`](auditsphere-execution-index-task-board-current.md), is generated by `scripts/docs/build-task-board.py` from the card front matter, the STE story headings and the audit record [`auditsphere-execution-report-task-codebase-audit-current.json`](auditsphere-execution-report-task-codebase-audit-current.json). `python3 scripts/docs/build-task-board.py --check` reports whether it is current.

Verified in this slice: the three docs validators pass (188 Markdown files); `task_status.py validate` reports no errors; the board check reports current. Not run: the .NET and Angular suites.

Next action on the critical path: the owner decides the T002 variation. Card step 3 and several downstream contracts require MediatR and bUnit, which AGENTS.md section 5 bans. Until T002 is COMPLETED, the helper keeps every later R2R card NOT_STARTED.

## Native client opening-balance maker/reviewer journey — 2026-10-07

The native accounting workspace now loads and displays the immutable opening
manifest for a selected client period, lets a preparer enter balanced approved
chart account codes with source reference/SHA-256, and offers a separate
reviewer an exact-manifest approval action. The UI prevents self-approval and
stale/closed-period approval. Approved openings appear in Trial Balance opening
columns separately from current-period movements. Angular accounting tests
passed 21/21, the production UI build passed with the existing Commercial
Settings stylesheet budget warning, and the PostgreSQL-backed API-hosted
Playwright journey passed 1/1 across maker, reviewer, retained approval and the
Trial Balance (125 opening debit/credit; zero period movement).

Evidence files are not uploaded by this workflow. Supporting opening AR/AP
invoice-level detail and reconciliation, prior-period carry-forward, correction
revisions, wider role/scope and recovery matrices, full solution regression and
production acceptance remain open. VAT/tax and ancillary capabilities remain
optional. Volatile results are recorded under
`verification.clientOperationalOpeningBalances` in `status.json`.

## Native client opening-balance snapshots — 2026-10-07

A client-period opening-balance API now records a balanced, chart-bound opening
manifest with an external evidence reference and SHA-256 metadata. Creation is
fenced to the current open period revision; a separate user must approve it.
Unapproved snapshots block the native GL/TB, and approved opening balances are
reported separately from period movements. PostgreSQL checks covered stale
revision, invalid balance, duplicate opening, self-approval, duplicate approval
and immutable-content rejection. The focused Domain test passed 1/1, API Release
build passed with zero compiler warnings/errors, OpenAPI contract tests passed
5/5, and EF reports no pending model changes. The OpenAPI artifact was refreshed.

The Angular maker/reviewer workflow has since passed a focused browser journey; see the newer handoff section above. Evidence bytes are not ingested. Supporting AR/AP detail, prior-period carry-forward, broader role/scope and recovery matrices, full solution regression and production acceptance remain open. VAT/tax and ancillary capabilities remain optional and do not block core bookkeeping.

## Angular materiality stale-source handling — 2026-10-07

The materiality journey now replaces the approved mapping after independent
approval and refreshes the audit plan. The UI marks the prior calculation stale,
explains that it no longer supports difference evaluation, removes the approval
action and omits FSLI risk stratification rather than applying old thresholds to
the replacement mapping. The PostgreSQL-backed Angular Playwright journey passed
1/1. The source/action row remains partial for calculation failure/recovery and
the broader materiality role matrix; the full solution and EF model-drift checks
were not rerun.

## Angular materiality policy refusals — 2026-10-07

The audit-plan browser journey now submits a revenue rate above its configured
policy limit, TE below 50%, and SAD below 3% through the Angular form. Each
request displays the corresponding policy error, and PostgreSQL confirms that
the refused attempts created no materiality assessment or calculation. The
journey then restores valid inputs and completes the existing successful
calculation and independent Partner approval flow. The PostgreSQL-backed
Playwright journey passed 1/1. The MaterialityEnginePanel source/action row
remains partial for calculation failure/recovery and broader
role-matrix evidence; full solution and EF checks were not rerun.

## Angular risk-routing independent review — 2026-10-07

Risk-routing projections now include the current assessment author. Angular hides
the Partner-clearance action when the signed-in Partner assessed that same risk;
the Application command continues to reject direct self-clear attempts. A
PostgreSQL-backed API-host Playwright journey passed with the fresh production
Angular bundle, checking both the hidden control and HTTP 403 denial. The Angular
suite passed 572/572, the Release solution build completed with zero warnings or
errors, and the Angular production build passed with the existing Commercial
Settings stylesheet budget warning. The source/action row remains partial for
its remaining role/scope, stale/revocation, recovery, accessibility and locale
gaps. Full solution regression, current EF drift and manual assistive-technology
acceptance were not part of this slice.

## Records Archive bounded manifest paging — focused verification passed — 2026-10-07

Archive reads now request no more than 100 ordered manifest rows and return a
total count plus an ordinal cursor. The Angular detail page appends subsequent
pages on demand, and a PostgreSQL browser journey covers a 205-entry
100/100/5 sequence with ordering and duplicate checks. The focused records
contract suite passed 6/6, including a transient next-page error followed by
an explicit successful retry that preserves then appends rows. Angular
template compilation passed with
`npx ngc -p tsconfig.app.json --noEmit`. The production build passed when run
outside the restricted sandbox; its initial bundle is 349.89 kB raw / 94.09 kB
estimated transfer. The commercial settings stylesheet remains above its
component warning budget (7.53 kB vs 4 kB). The focused PostgreSQL Playwright
journey passed 2/2, including the 205-entry 100/100/5 sequence, ordered rows,
duplicate checks and empty/incomplete-manifest presentation. Its first run
exposed an ambiguous test selector because the page has two status messages;
the assertion now targets the archive summary explicitly. The separate API
journey passed 1/1, verifying exact cursors, safe rejection of a negative
cursor, and identical foreign-versus-guessed-ID denials. The built-in browser
tab could not reach localhost:5105, so that manual visual check remains open.
The Records Archive source row remains partial and the migration gate remains
`NOT_READY`.

The full Angular CI suite also passed 557/557 tests across 108 files against
the current shared working tree, including the parallel finance UI edits. The
result is attributed to master `0c4391ba` plus those uncommitted working-tree
changes, not a clean commit. A production-build retry inside the restricted
sandbox reproduced the esbuild deadlock (exit 134); the earlier outside-sandbox
build remains the latest bundle pass. After the Records retry test was pushed
as `0833f8a2`, an outside-sandbox production build passed again and the focused
PostgreSQL Playwright archive cohort passed 2/2 against the fresh bundle. The
full PostgreSQL-backed solution and EF model drift checks were not rerun.

## Shared confirmation cancellation and validation — a27c3102

The Users & Access browser journey now verifies that closing the revocation
dialog leaves the local grant active and that revocation remains disabled until
the administrator enters a valid reason and explicitly confirms the reviewed
grant. It then completes revocation, verifies immutable evidence, and checks
that an already-open protected staff session is invalidated. The Practice Leads
journey verifies that canceling lead qualification leaves the lead in `NEW`,
then reopens the confirmation and completes qualification without implying
client acceptance or access.

Focused Release API-host Playwright journeys passed **1/1** each against owned
isolated PostgreSQL databases: revocation in 47 seconds and lead qualification
in 38 seconds. The built-in browser could not reach `localhost:5099` because the
host refused the connection, so the interactive Development session was not
available for these checks. The shared confirmation source row remains
`PARTIAL`: complete role/scope/expiry/cross-firm and unknown-result coverage
across both callers, human screen-reader and wider-locale acceptance, and the
broader retirement gates remain open. The full solution suite, Angular
production build and EF drift check were not rerun for these test-only slices.
Evidence is recorded in
[`auditsphere-migration-blazor-confirm-dialog-source-review.md`](../migration/auditsphere-migration-blazor-confirm-dialog-source-review.md)
and `status.json`.

## Records Archive incomplete-manifest state — 21e85a17

Added a PostgreSQL-backed Angular browser journey for an empty manifest marked
`INCOMPLETE`. It verifies the persisted reason, zero-entry empty state,
profile/version and digest, and distinct `Not observed` / `Not requested`
records evidence; no complete-archive claim is shown. The focused Release E2E
journey passed 1/1 in 33 seconds with no page or console errors. This closes
empty/incomplete presentation evidence only; large-manifest and failure/recovery
coverage, authorization matrices, human accessibility/locale and production
gates remain open. The full solution and EF checks were not rerun. Evidence is
recorded in `status.json` and the Records Archive/Release source review.

## Workpaper autosave and discard decision race — 6d1f91ad

A browser journey exposed that a pending Workpaper autosave could fire while
the unsaved-edits dialog was open, persisting content before a discard choice.
The navigation guard now cancels the debounce before asking the user. The
Workpaper Angular unit suite passed 13/13 and the focused PostgreSQL/API-host
Playwright class passed 4/4 in 1 minute 19 seconds. A simulated 409 discard
refusal leaves the route and active revision-1 server draft unchanged; typed
edits are not persisted. Direct discard and discard-before-navigation also
pass. The built-in browser rejected the local URL under its security policy,
so no fallback navigation was attempted; authenticated behavior was verified
in the owned Playwright sessions. Production Angular build and full solution
regression were not rerun; the Workpaper row and Blazor retirement remain
partial/NOT_READY. Evidence is recorded in `status.json` and the Workpaper
source review.

## Workpaper draft discard and navigation recovery — 1d05ed4c

Added a PostgreSQL-backed Angular/API-host browser journey for the Workpaper
discard action. The journey loads an existing server draft, discards it through
the page, verifies the editor clears, and confirms the persisted draft is
`DISCARDED` with no submission created. The navigation guard also discards the
saved draft before routing and keeps newly typed edits out of persistence. The
focused Release Workpaper E2E class passed 3/3 in 1 minute 11 seconds,
including saved-response recovery. The discard journeys had no browser errors.
This closes the successful direct and navigation discard evidence gaps only;
the Workpaper source row remains partial for
the broader role/scope, failure/recovery, accessibility and production gates.
The full solution suite and EF drift check were not rerun. Evidence is recorded
in [`auditsphere-migration-blazor-workpaper-source-review.md`](../migration/auditsphere-migration-blazor-workpaper-source-review.md)
and `status.json`.

## Route layouts and accounting navigation crosswalk — 15e4bc61

Reviewed and hash-checked the legacy `AccountingNavigation`, `ClientLayout` and
`PublicLayout` against their Angular route and shell owners. Accounting
destinations now live in the shared staff navigation, client sessions see only
the portal destination, and root/setup handling remains in the explicit Angular
shell/API ownership model. Existing navigation and route-sweep journeys cover
the main destinations and role-specific shell behavior; the full legacy
label/active-link matrix, public-state parity and human assistive-technology
acceptance remain open. No source code changed in this review.

The focused PostgreSQL browser journey was not rerun: the configured local
database port did not respond, and the existing `postmaster.pid` referenced an
active process that could not be verified as PostgreSQL. I left the lock file
and database untouched. Per-artifact hashes, dispositions and the local
verification boundary are recorded in
[`status.json`](status.json) under
`verification.angularBlazorCurrentRetirementGate.layoutAndNavigationSourceReview`.
The retirement gate remains `NOT_READY`.

## Angular shared status-chip semantics — 15e4bc61

Angular's shared `StatusChip` now applies the same informational, success,
warning and error categories as the retained Blazor component, with a neutral
fallback. The visible status text remains available to assistive technology and
does not depend on color. Focused component coverage exercises every tone and
the neutral fallback. The Angular unit suite passes at this slice; the full
production optimization still aborts locally, while Angular's development
server compiles and serves the current source.

The built-in browser opened that development server and reached the Angular
unauthenticated shell. Its session request was refused because no API host was
running, so authenticated status chips were not visually verified in this
browser run. Detailed test/build/browser evidence is recorded in
[`status.json`](status.json) under
`verification.angularBlazorCurrentRetirementGate.angularStatusChipSemanticTones`.
The expanded source review keeps the retained shell/support artifacts
`PARTIAL`; supporting-file analysis remains open. Full migration
acceptance, production canary/rollback, human assistive-technology and locale
checks, live Microsoft gates and owner acceptance remain open. The Blazor host
stays present and retirement remains `NOT_READY`.

## Blazor consolidation source/action review

Reviewed the last two unanalyzed presentation artifacts: the group consolidation overview and
advanced schedule/execution workflow. The current Angular/API/Application owners are mapped in the
code map and source review. Focused PostgreSQL-backed browser journeys confirm active group-scope
isolation, nondisclosing denial, route-change clearing and responsive rendering. Both rows remain
partial: complete per-command role/error/unknown-outcome coverage, overview-field assertions,
bounded-load acceptance, report timestamp parity, assistive-technology review and production/owner
acceptance remain open. The current gate is still `NOT_READY`; the Blazor host remains retained.

## Project task progress: current-access revocation and clearer status summaries

The Administrator-only project task progress workspace now offers an explicit refresh action,
accessible progress-bar key, per-section completed percentages and active/pending/blocked totals,
and blocked explanations in audit, phase, and shared-foundation lists. Unmapped modules state
accessibly that implementation progress is not measured. Unit coverage checks malformed payloads,
stale state, refresh recovery and cancellation after session invalidation.

Verification at `ffe3a425aa65812b9d29eb5bc7b4693048bdea20`: Angular progress tests passed 4/4;
the production build passed with the existing Commercial Settings component-style budget warning
(7.53 kB against 4 kB). The PostgreSQL-backed API-host browser journey passed 4/4: Administrator
access and non-administrator denial, concurrent expiry and one-time revocation evidence, immediate
stale-page clearing after role revocation followed by fresh-sign-in denial, and a cross-firm
misbound-grant denial. Tests used synthetic identities and owned isolated databases. The built-in
browser rendered the safe unauthenticated state but had no session for manual inspection of the
protected tracker; the authenticated behavior was verified in Playwright. A full solution regression
was not rerun. Corrupt-file recovery, human assistive-technology and wider-locale acceptance remain
open. Blazor retirement remains `NOT_READY`.

## Reproducible native GL snapshot and filters (CA-23 partial)

Native GL pages carry the database-assigned posting high-water mark captured in a repeatable-read
snapshot. A commit-serialized posting trigger prevents a later visible post from landing below a
previous page's cursor. Trial Balance and movement totals use the same cursor; a later backdated
posting cannot shift later pages. Migration backfill assigns sequences to existing posted journals
without changing their captured accounting content.

The scoped UI/API accepts date range, account-code range, source kind, reference, and counterparty
identity filters. The report returns both matching detail-line count and total posted lines in the
date range. Date and account-code bounds define the Trial Balance basis; source, reference and party
filters narrow detail lines only. Account filtering preserves earlier posted activity in opening
balances. Source distinctions come from immutable journal-origin records; invoices, credits,
settlements and reversals remain native client postings rather than imported source GL or audit
adjustments. Counterparty selection currently accepts the profile's party identifier. Snapshot
drill-through now adds exact native invoice/credit/settlement origin IDs, submitted revisions,
manifest hashes, command-intent hashes, and recorded source-evidence IDs/hashes/references. The UI
keeps manifest, intent, and evidence hashes distinct and states that referenced evidence bytes are
not embedded in the journal snapshot. The posted-invoice database guard now
excludes the database-assigned posting sequence from submitted business-content equality checks;
an additive follow-up migration applies this fix to already upgraded databases.

Verification in an isolated temporary copy: focused PostgreSQL ledger/filter and pre-migration
backfill tests pass 2/2; invoice, supplier invoice, both credit-note types and manual customer/supplier
settlement workflows pass 6/6 with the updated posting guard; the Angular operational journal suite
passes 18/18; API OpenAPI tests pass 5/5 and the generated contract includes the GL filters and
source-lineage schema. The worktree pins SDK 10.0.300 while only
10.0.400 is installed, so the project file was left unchanged and an isolated copy used the
installed SDK. Angular production and development builds repeatedly exited 134 without compiler
diagnostics. The extended Playwright journey then served the prior UI bundle and could not find the
new filter control; it timed out before issuing filter requests, so browser verification remains
unverified for the updated UI. CA-23 durable full-result export and retrieval of exact source evidence
versions/bytes remain open.
This does not complete CA-23 or CA-24; full scope remains unmerged, and tax/ancillary modules remain
optional.

## Partial client open-item allocation and imported settlement evidence (CA-19)

Client-accounting now has a client-scoped open-item workspace for approved posted invoice and credit-note balances, partial allocations, on-account residuals, as-of due-date status, independent review, and exact audited unallocation. It can also link an already imported, sealed receipt/payment line when the source has one approved AR/AP control line and an independently matching cash movement. That path references the existing ledger entry and creates no second cash posting. Cross-client, cross-party, cross-currency, stale-preview, and over-allocation requests fail closed. Tax and ancillary modules remain optional.

This slice now also permits a specifically evidenced, externally completed client receipt or supplier payment to be recorded as a balanced two-line client journal. It uses the approved client AR/AP control and a non-control cash asset, has immutable external reference/evidence, command idempotency, independent journal submission/review/posting, and can appear as an allocatable open-item source. It never initiates or verifies a bank transfer. Posting decision and journal-post timestamps share one retained instant so the database can verify the exact approval. Existing invoice and credit-note posting guard branches remain in force.

Local evidence in the managed `codex/client-accounting-workflow` worktree: the customer receipt and supplier payment journeys, supplier-invoice and both credit-note regression journeys, and settlement-migration rollback check passed 6/6. Customer receipt allocation retained a single cash posting and left the expected residual; supplier payment debited AP and credited the client cash asset. The API and referenced projects built with 0 warnings/errors in the isolated `SettlementCheck` configuration; Angular production build passed with the existing Commercial Settings stylesheet budget warning; EF reported no pending model changes; the API contract serialization test passed and the generated OpenAPI contract diff is clean. The full PostgreSQL-backed solution suite and API-hosted Angular manual-settlement journey were not run.

CA-19 now includes allocation of an already-posted receipt or credit to an approved opening invoice. The additive migration extends the allocation target constraint and deferred validator; it accepts only the latest approved opening for the same client, party and currency, and retains immutable independently reviewed allocations. The application validates the opening manifest digest before preview and submission. Focused PostgreSQL evidence proves a partial receipt allocation against opening AR, the remaining opening balance, zero residual receipt, and exact AR-to-ledger reconciliation. Opening details must tie exactly to the approved AR/AP control balance; aggregate-only openings continue to disclose incomplete aging. Allocation approvals have no separate accounting-effective date, so historical cutoffs use current approved allocations. Prior-period opening carry-forward, the full solution regression, production migration and external acceptance remain outstanding. This is a local feature slice, not full-epic or production acceptance.

## Complete frozen-file write barrier with serialized freeze (STE-REM-09)

The pending-features review's frozen-write findings are closed. Every professional mutation boundary
now carries a consistent frozen-state check: new SRMs, Partner clearance, opinions, signatures,
shares, comments, resolutions, acknowledgements, signed-representation upload/verify, confirmation
criticality and the confirmation status/response/alternative/dispatch/closure commands, note events
(respond/resolve/reopen), procedure result submission and review, item tests, audit differences,
area assessments, and the existing PBC/activity/currency/confirmation-workspace guards. Each refusal
records a distinct frozen-access attempt; refusals inside a caller's transaction commit the attempt
record before returning so the evidence survives. The professional-work authorization itself is
frozen-aware: a frozen engagement reports `protected-state.denied` (with a neutral access label)
instead of the generic professional-work block, after scope and role validation, so every
professional-work command reports the frozen state consistently. The frozen-state check takes a
shared lock on the engagement row inside the caller's transaction and the worker freeze takes the
same row exclusively, so a freeze can never be overtaken by a write that checked earlier.

Verification in an isolated worktree at the pushed STE-REM-08 head plus this slice: the new
PostgreSQL test passed 1/1
(`ConcurrentFreezeSerializesWithWrites_AndFrozenFileRefusesProfessionalMutations` — the shared-lock
serialization probe, the worker freeze through the public discovery/handler path, and eight refused
boundaries with recorded attempts and unchanged business rows), the existing 60-day freeze journey
passed, and the broad regression across the deliverables, program, fieldwork, materiality,
confirmation, difference, area-assessment, review-note, planning and authorization classes passed
64/64. Release builds passed with zero warnings and errors and the OpenAPI contract drift check
reports the committed artifact current (no route changed). The full Domain and Api suites and the
browser journeys were not rerun: the change surface was the freeze service, the professional-work
authorization branch and the named command boundaries. Tests used owned isolated PostgreSQL schemas;
no Development or production state was changed. Blazor retirement remains `NOT_READY`.

## Critical-unreturned confirmations keep blocking the report and signature (STE-REM-08)

The pending-features review's confirmation finding is closed. Confirmation rows now carry the distinct
returned-and-independently-evaluated response fact (the current response revision with an
Agreed/Difference decision independently reviewed by another user); criticality, returned-response
evidence, independent evaluation and workflow closure remain separate facts. A case that currently
stands Critical closes only on that returned and evaluated response — reviewed alternative work keeps
its appropriate noncritical path — and a critical case without it keeps holding the auditor report
even after alternative-only closure or a later criticality reassessment, with the holding letter
generated for the exact blocking set. The signature rechecks the current critical set before the
staleness fence, so a report generated before a case became critical cannot be signed. Criticality
reassessments retain every actor/rationale history row. The existing signing fixture now closes its
critical confirmation through the real response record/review/closure path instead of a direct status
edit, and the committed OpenAPI artifact was regenerated for the parallel session's four additive
AS-COMP-16 fieldwork endpoints (the drift check passes with no removals).

Verification in an isolated worktree at the pushed AS-COMP-16 head plus this slice: the new focused
PostgreSQL test passed 1/1
(`CriticalUnreturnedConfirmationKeepsBlockingAfterAlternativeClosureAndCriticalityReassessment`), the
full `AuditDeliverablesTests` and `AuditConfirmationCommandIsolationTests` classes passed 11/11, the
Release builds of the Api and test projects passed with zero warnings and errors, and the OpenAPI
contract drift check reports the committed artifact current (the additive dashboard field needs no
Angular change: the decoders read only their declared keys). The full Domain and Api suites and the
browser journeys were not rerun: the change surface was the confirmation closure gate, the dashboard
fact, the report/signing predicates and the focused tests. Tests used owned isolated PostgreSQL
schemas; no Development or production state was changed. Blazor retirement remains `NOT_READY`.

## Procedural workpapers, evidence links, and safe collaboration (AS-COMP-16)

Fieldwork procedure tailoring, workpaper generation, evidence linkage, and independent review lifecycle are verified (§§4.3.2–4.3.3, AS-COMP-16):
- Procedure tailoring (`AuditProgramService.TailorProcedureAsync`): allows tailoring title, custom wording, and applicability rationale under planning authorization. Updates acquire an exclusive row lock (`FOR UPDATE`); tailoring an already reviewed or submitted procedure reopens testing to `InProgress` and increments `CurrentResultRevision`, safely invalidating stale conclusions.
- Workpaper generation (`AuditProgramService.GetOrCreateWorkpaperAsync`): initializes a dedicated `Workpaper` entity linked to the planned procedure and moves the procedure to `InProgress`. Idempotent requests safely return the existing workpaper ID.
- Evidence linking & records queries: `AuditRecordQueries.WorkpaperAsync` returns linked digital PBC evidence (with SHA-256 content digests) and physical evidence indexing (file index, box reference, location movements).
- Submission synchronization: `AuditPlanningService.SubmitWorkpaperAsync` updates linked procedure status to `Submitted` in lockstep.
- Fieldwork API endpoints: exposed `/api/ui/procedures/{id}/tailor`, `/api/ui/procedures/{id}/workpaper`, `/api/ui/procedures/{id}/results`, and `/api/ui/procedure-results/{id}/review`.
- UI updates: `workpaper.ts` decodes and renders linked digital evidence and physical storage references; `fieldwork.ts` decodes `workpaperId`.
- Verification: PostgreSQL-backed unit tests `ProcedureTailoringAndWorkpaperGeneration_EnforcesLifecycleAndInvalidation` (in `AuditProgramWorkflowTests`) and `LateAndReopenedNotesBlockClearanceUntilRespondedResolvedAndReviewed` (in `AuditDeliverablesTests`) pass alongside the full 104-suite Angular frontend test run and .NET test suite. Release build compiles cleanly with zero warnings/errors, and EF Core model has zero pending migrations.

## Engagement-scoped CRM, hierarchy and client-finance reads (STE-REM-03)

The pending-features review's commercial scope findings are closed. Correspondence-recipient
resolution now requires current internal commercial scope for the client: it reuses the
target-aware `AuthorizationDecision` (client-level coverage, `CommercialRoles`, internal-only),
returns a nondisclosing `Denied` status when scope is missing, revoked or engagement-only, and
the authorized-override path no longer bypasses that check. The resolve endpoint maps `Denied`
to a 403 `scope.denied` refusal; no contact identity is returned on refusal.

Organization-hierarchy exploration now authorizes every counterpart client before returning any
relationship metadata. Counterpart nodes outside the actor's current commercial scope are
## Late and reopened review notes block completion (STE-REM-06)

The pending-features review's finding that a regenerated Summary Review Memorandum could bypass a
late or reopened review thread is closed. The canonical completion evaluation now includes every
unresolved thread on the engagement as a `review-note:{id}:unresolved` blocker — across all result
revisions and including reopened threads — and the SRM facts use the same engagement-level
open-thread count, so regeneration, applicability changes and clearance comments never resolve a
thread. A note added after the work was reviewed, or a thread reopened afterwards, returns the
exact procedure and result to `CHANGES_REQUIRED` while retaining the earlier review decision, so
Partner clearance, signing and publication stay blocked until the required response, reviewer
resolution, resubmission and renewed review restore the current work. A frozen file refuses the
note attempt under the existing write barrier and records the refused attempt separately.

This slice landed inside the parallel session's AS-COMP-16 commit `d51c189f` (its verification
covered the combined tree); the focused STE-REM-06 evidence was verified on an isolated worktree
before that commit and re-verified at `d51c189f`: the two new PostgreSQL tests passed 2/2
(`LateAndReopenedNotesBlockClearanceUntilRespondedResolvedAndReviewed`,
`FrozenFileRefusesReviewNoteAttemptsAndRecordsTheRefusedWrite`), the full `AuditDeliverablesTests`
and `AuditReviewNoteIsolationTests` classes passed 10/10, `AuditProgramWorkflowTests` passed 4/4,
and the OpenAPI contract drift check reports the committed artifact current (no route changed).
Tests used owned isolated PostgreSQL schemas; no Development or production state was changed.
Blazor retirement remains `NOT_READY`.

omitted entirely — no name, identifier or existence disclosure — while authorized relationships
(including the holding parent seen from an authorized subsidiary) remain visible.

Client-portal finance reads now carry exact grant scope: a client-wide `ClientUser` grant covers
the client's agreements while an engagement-scoped grant only covers its own engagement's
agreement — never a sibling engagement's agreement, invoice, receipt or amount. Commercial-document
downloads for client users now require the exact client and, for engagement documents, the exact
engagement; the permissive null-client grant fallback is removed; and only explicitly client-shared
documents (official payment receipts) are downloadable — internal quotations, tenders and letters
are refused.

Verification in an isolated worktree (the shared checkout carried another session's in-flight
planning/fieldwork edits): the three new PostgreSQL-backed tests passed 3/3
(`RecipientResolution_RequiresCurrentCommercialScope_AndOverridesDoNotBypass`,
`OrganizationHierarchy_HidesCounterpartNodesOutsideCurrentCommercialScope`,
`PortalFinanceAndReceiptDownloads_RespectExactClientAndEngagementScope`), the full
`ClientRelationshipsAndRoutingTests` and `CommercialWorkflowTests` classes passed 16/16, the
routing/portal API contract classes passed 5/5, the Release builds of the Api and both test
projects passed with zero warnings and errors, and the OpenAPI contract drift check reports the
committed artifact current (no route changed). The full Domain and Api suites and the browser
journeys were not rerun: the change surface was the named Application reads, one endpoint
response mapping and the two touched test classes. Tests used owned isolated PostgreSQL schemas;
no Development or production state was changed. Blazor retirement remains `NOT_READY`.

## Comparative split financial statement dashboard (AS-COMP-15)

The interactive Financial Statements workspace now provides a comparative split dashboard (§4.3.1, AS-COMP-15):
- Upper Statement of Profit or Loss (P&L) and lower Statement of Financial Position (B/S) are rendered simultaneously in the desktop workbench (responsively stacked on narrow viewports).
- Columns for each line and section summary show Current Year balance, Prior Year comparative balance, Absolute Variance, and Percentage Variance adhering to sign convention `(Current - Prior) / |Prior| * 100%`, with zero prior denominator displayed as `N/A` and unavailable prior data displayed as `—`.
- Prior period comparative figures are resolved systematically from the validated financial package comparative reference, earlier validated financial package for the client/currency, or earlier approved mapping and sealed trial balance.
- FSLI rows display risk band badges ("Low", "Medium", "High") derived from engagement materiality assessments or FSLI defaults, along with assigned performer and required reviewer ranks.
- Procedure execution status indicators ("Reviewed", "In progress", "Planned", "Changes required", "No procedures") reflect underlying audit substantive testing progress.
- Distinct interactive triggers are provided per row: `[AR Test]` (navigating to analytical review preparation) and `[Audit Workprogram]` (navigating to fieldwork substantive procedures).
- Backend endpoint `GET /engagements/{id}/statements/split` returns both sections simultaneously.
- Verified with PostgreSQL-backed API tests (5/5 passing in `StatementReviewApiTests`), full Angular test suite (541/541 passing across 104 suites), Angular production build, Release solution compilation (0 errors, 0 warnings), and zero EF Core model drift.

## Statutory milestone planning and 60-day archive freeze (AS-COMP-10)

Statutory filing deadlines are now explicitly stored and never silently assumed or hardcoded (§4.2.2, AS-COMP-10). The engagement milestone plan entity (`EngagementMilestonePlan`) captures the explicit statutory filing cutoff, fieldwork commencement target, draft report delivery target, final signed report date, and the mandatory 60-day archive deadline under ISA 230 (§4.4.3).

A pure calculator (`StatutoryMilestoneCalculator`) evaluates chronological consistency and overridable scheduling warnings:
- Enforces strict chronology: period end < statutory filing cutoff, fieldwork commencement >= period end, draft delivery >= fieldwork commencement, final signed report >= draft delivery, final signed report <= statutory filing cutoff, and archive freeze >= final report.
- Detects compressed scheduling conditions (fieldwork window < 14 days, review window < 7 days, statutory filing buffer < 5 days).
- Distinguishes forbidden validation errors from overridable scheduling warnings: saving with scheduling warnings strictly requires a documented partner or manager override reason.
- Requires documented adjustment reasons whenever altering calculated defaults.

The application service (`StatutoryMilestoneService`) enforces scope-checked authorization (`Partner`, `Manager`, `SeniorManager`, `Administrator` can configure; planning roles can preview/read; unauthorized access is denied). Angular decodes the milestone plan, displaying the operational schedule, 60-day archive freeze, scheduling warnings, override reasons, and an interactive schedule configuration form.

Focused PostgreSQL-backed database integration and calculator tests passed (9/9). Full Angular unit tests passed (540/540 tests across 104 suites) and production build succeeded. Release solution build passed with 0 warnings and 0 errors, and EF Core reports no pending model changes.

## Reviewed client posting account roles

Client books now support immutable role proposals and independent approval/rejection. Approved intervals select compatible posting accounts from an approved client chart; no financial-statement meaning is inferred from account-number prefixes. Roles cover AR/AP, tax recoverable/payable, revenue, purchase expense/asset, retained earnings, rounding and FX. Tax and ancillary roles remain optional.

Client locking and database checks fence overlapping approvals, self-review and control activation over posted generic activity. Generic native journals cannot create unexplained AR/AP activity: preparation, preview/submission/post validation and a specific database posting trigger refuse that path. Earlier drafted journals become ineligible after an applicable control role is approved. Angular offers bounded account search, paged retained history, explicit proposal assent and independent review; unconfirmed commands require fresh history before further preparation. Focused PostgreSQL, Angular, API contract and automated browser checks passed. Exact source and observed counts live in `verification.clientAccountRoles` in `status.json`.

Two managed solution-build attempts were interrupted when a concurrent process merged the branches and removed their checkouts. The preserved test/contract commit passed the full solution build in an independent temporary verification checkout. Work continues in a new managed worktree, with user-authorized cleanup coordination requested. This role slice does not implement default/system invoice postings, AR/AP open items, approved-role replacement or end-date amendments. The full epic remains active; the final merge is pending completion. The calculator-baseline regression continues separately and must not be attributed to this newer model.

---

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

## Commercial acceptance and lifecycle privacy hardening — 2026-10-12

Code checkpoint `58e3d443df8b8de8a223294889f0459ca1ec015d` hardens the WF-01/WF-03 commercial keys. Unlinked engagement proposal lookup is restricted to the engagement's service route, and a linked fee agreement must belong to the same client. A commercial acceptance now stands only when the proposal has sent/response timestamps, the newest approved quotation matches its fee, and an immutable dispatch record binds that quotation and the same nonempty offer hash cited by both sides. Engagement-letter and fee-agreement creation enforce that same evidence; the fee workspace shows the corresponding blockers, and onboarding readiness receives the API host's automatic-invoice setting. Lifecycle blocker text is now redacted for actors without a firm-wide management grant across both the canonical lifecycle endpoint and workflow projection. Workflow read refusals map request/scope/state failures to HTTP 400/403/409, with unexpected errors left as 500. Angular timestamps now require an explicit UTC offset.

Fixtures and regression cases were aligned to the exact dispatch contract, including missing-dispatch refusal, route isolation, current quotation binding, paid-advance readiness, and restricted lifecycle details. Verification on the code checkpoint: Release solution build succeeded with 0 errors and 6 NU1900 vulnerability-feed warnings; `WorkflowReadHttpStatusTests` passed 7/7; the targeted Angular decoder suite passed 6/6; `git diff --check` passed. GitHub Actions CI run [38201582511](https://github.com/nirzaf/AuditSphere/actions/runs/38201582511) passed: Docs · Health, Build · Angular (production build and full specs), and Build · .NET Release (EF drift, API publication, and OpenAPI drift) all succeeded.

Limits: PostgreSQL-backed Domain and E2E cases were compiled but not executed. PostgreSQL was not listening on 127.0.0.1:5433; startup was refused because the existing data directory reports a pre-existing shared-memory block. The local Angular production build exited 134 without compiler diagnostics, although the CI production build and full Angular suite passed. Local Markdown health and filename checks still report only the separately staged user file `STE_Audit_Management_Requirements_v2.2.md`; it was not modified or committed. Narrative metrics, UI inventory, JSON parsing and `git diff --check` passed. No local browser journey or full .NET suite is claimed. The v2.2 workflow epic remains PARTIAL; production, external-provider, and professional acceptance are still open.

## v2.2 workflow experience, PRs A–C — 2026-10-11

The engagement page now opens on a read-only seven-phase workflow projection and independently evaluated Dual-Key readiness cards, with the eleven-stage lifecycle kept as a technical panel. The projection derives from the existing canonical stage and persists nothing new; no stage is writable. Advance billing no longer opens on a Partner clearance alone: both keys must be current on the exact dispatched offer. The advance and activation cards read the same milestone state and engagement link that activation checks. The protected letter, fee-agreement and activation commands are unchanged.

Not yet delivered: workflow task persistence and templates (WF-04 to WF-06), queues, automation and recurrence (WF-07 to WF-11), completion and archive cards (WF-13), and the lead-level pre-engagement journey. One owner decision is open: the protected letter guard applies offer identity only when a dispatch record exists, while the read model requires it always. Evidence and the open review findings are recorded in `status-archive-2026-10.json` under `keys.verification.workflowExperienceV22`.

## STE v2.2 delegated owner decisions — 2026-10-12

ADR-0017 records the STE v2.2 decisions the owner delegated. Three keep an earlier decision: WhatsApp stays a lead-channel label, client users sign in through Microsoft with no emailed password (ADR-0003), and the materiality benchmark is not normalised (ADR-0011). The rest set new behaviour: opinion, signing, clearance and bundle assembly need the staffed Engagement Partner and fail closed without one; the continuance bank gains a legal-notice question and a fees-settled confirmation, both with evidence; a client acceptance records the channel it arrived by; the firm working week is Sunday to Thursday; and a critical accounting estimate is a flag the assessor records, which bands the risk Red.

Migration `SteV22OwnerDecisions` adds two columns, extends the band check constraint and adds the two questions where the continuance bank already exists. It has run only against the test database. Operators should expect three effects: an engagement with no staffed Engagement Partner cannot reach the opinion until one is staffed; a continuance checklist in progress needs the two new answers; and capacity figures change for ranges that include a Friday or a Sunday. Evidence and open items are in `status-archive-2026-10.json` under `keys.verification.steV22OwnerDecisions`.

## Sunday week start, corrected commercial tests and specification rename — 2026-10-12

Allocation weeks now start on Sunday, at the owner's request, so the Sunday to Thursday working week sits in one week bucket. `ResourceGridCalculator.WeekStart` returns the Sunday, the `staff_allocations` check constraint requires one, and migration `QatarSundayWeekStart` moves each stored Monday week one day earlier, to the Sunday-start week that contains it. Planning receipts committed before the change keep the Monday they recorded. ADR-0017 is amended. Operators should expect the resource grid and every stored allocation to show a date one day earlier after the migration; the planned minutes do not change.

Two `CommercialWorkflowTests` that failed on master were test defects and are corrected with no product change: one read a stale tracked proposal after raw updates, and one asserted the Partner-only activation flag for a Relationship Manager. The STE v2.2 source specification moved from the repository root to `docs/requirements/auditsphere-requirements-source-ste-audit-management-specification-v22.md`, and the filename validators no longer carry an exception for it.

The full .NET suites were not run locally; the owner asked for test execution to be left to CI. Focused evidence and what was not run are in `status-archive-2026-10.json` under `keys.verification.qatarSundayWeekStart`.

## Practice management completion program, first slice — 2026-10-12

The owner supplied a completion program (stories PM-00 to PM-17, with an appendix of candidate gaps). It is stored as `auditsphere-execution-user-stories-cloud-practice-management-v22-proposed.md` and reconciled against the code in `auditsphere-execution-tracker-practice-management-v22-crosswalk-current.md`. Most open rows wait on an owner or professional decision, on live infrastructure, or on a scope choice; the crosswalk names which.

Three decision-free gaps are closed. The firm trial balance report returns one profit and loss row per accounting month with the same month a year earlier as comparator, and the months add up to the range totals. Time capture, budgets, budget previews and quotations share one rate lookup, so an STE alias such as Senior prices the same everywhere and an unknown role still fails closed. The engagement dashboard names the people staffed at the level that owns the current step and shows the days left to the scheduled freeze. No lifecycle rule, gate or stage mapping changed, and there is no migration.

The full .NET suites were left to CI. Evidence, what was not run and open items are in `status-archive-2026-10.json` under `keys.verification.practiceManagementBaselineSlice`.


## Practice management program, third slice — 2026-10-12

A proposal can carry an optional delivery timeline. It is part of the creation receipt and is rendered on the brief quotation and the engagement letter, so it sits inside the offer the client accepts: a changed timeline is a new revision and needs a fresh acceptance. Proposals created without one keep their identity. Migration `ProposalTimeline` adds the column and has run only against the test database.

The work queue can show a loaded page as a list, a board by operational status, or a calendar by due date in Sunday-start weeks. The three arrangements regroup the same tasks, and a card offers only the task's own completion command. The engagement dashboard shows an expandable checklist per phase, with task progress beside the gate status and a plain statement when every task is done but the gate is blocked. Both sit behind `WorkflowExperience:Enabled`, which stays off by default, and neither was run in a browser.

The owner-supplied program and its crosswalk are in `docs/execution`; the crosswalk lists what remains and why. The full .NET suites were left to CI; evidence is in `status.json` under `targetedVerification.practiceManagementWorkflowViewsSlice`.

## RelationshipManager preparation — 2026-10-13

Existing client/firm grants now permit RelationshipManager blocked-shell preparation and client business summaries. Engagement-only scope cannot widen to the parent, and professional reads, decisions and activation remain separately restricted. The receipt publication guard enforces the same role policy. Evidence and isolated rollback are under `targetedVerification.workflowRelationshipManagerPreparation` in [status.json](status.json).

## Practice management program, second slice — 2026-10-12

ADR-0018 records the reporting decisions the owner delegated: approved non-billable and no-charge time carries hours and cost and no charge-out value; utilization is approved billable time over capacity in both the weekly grid and department analytics; and the receivables report ages every posted firm invoice, typed as advance, final, time or other. The ADR also lists the professional, legal and commercial decisions it does not make.

The engagement page has a communications panel backed by `GET /api/ui/engagements/{id}/communications`. It lists recorded outbound mail for the engagement, to engagement Partners, Managers and Administrators, without message bodies, and never words provider acceptance as delivery. The crosswalk now maps the twelve non-regression scenarios to named tests and states where none was found.

Operators should expect realization and contribution to appear where non-billable time used to make them unavailable, lower grid utilization for staff with non-billable time, and higher receivables totals where non-milestone invoices are posted. The full .NET suites were left to CI; evidence is in `status.json` under `targetedVerification.practiceManagementReportingSlice`.

## Annual task-plan references — 2026-10-13

Annual preparation now lists up to three currently published revisions from prior task-plan template identities, plus up to three prior schedule bases. They are reference-only: fresh plan review remains required and no tasks or deadlines are copied. Build and focused query evidence are recorded under `targetedVerification.workflowAnnualTaskPlanReferences` in [status.json](status.json). Template selection/pinning orchestration remains open.

## Annual preparation and scoped decisions — 2026-10-13

Annual shell preparation now proposes a contiguous next period from an exact client-owned source, separates copied references from fresh work, and uses existing reviewed creation and receipt recovery. Source identity is retained; prior approvals and audit evidence are not copied. Native Partner decisions target exact engagements and retain their scoped receipts. Evidence is under `workflowAnnualPreparationCore`, `workflowScopedAssessmentAuthoring` and `workflowRecurringRiskScope` in [status.json](status.json). WF-11 remains partial: approved template selection, reference suggestions, fresh-checklist orchestration still need work.

## Annual task-plan handoff — 2026-10-13

Confirmed annual creation receipts now lead to the new engagement’s reviewed operational plan. The explicit fragment loads the deferred panel; ordinary visits still use viewport loading. Published template choices are read through exact engagement scope rather than firm-wide template administration. Plan execution retains independent authorization, review assent, staffing/schedule fencing and immutable revision receipts. Focused evidence is under `targetedVerification.workflowAnnualPlanHandoff` in [status.json](status.json); annual contact/mapping suggestions, fresh-checklist orchestration and broader acceptance remain open.

## Workflow task queue and review rework visibility — 2026-10-12

WF-07 now has **Changes required** and **Under review** views derived from the exact linked audit procedure result. A newer submitted revision moves the linked task into Under review; an independent review decision removes it from that view. Task completion remains operational and never changes audit review. The exact-scope composite foreign key, unique result index and check constraint prevent cross-scope links and duplicate rework bindings. Migration `20261012080058_WorkflowChangesRequiredQueue` is source-only and has not been applied to a persistent database. `WorkflowExperience:Enabled` remains false by default.

Earlier current slices include WF-10 PBC task creation and optional reminders, WF-14 due-date history, and the workflow-template authoring screen. Their configuration, verification results and limitations are in `status.json` under `workflowTaskPbcFollowUp`, `workflowPbcReminders`, `workflowTaskDueDateHistory` and `workflowTemplateAuthoringUi`.

Verification: the focused PostgreSQL regression passed 1/1 across changes-required visibility, resubmission into Under review, and removal after independent review. API/OpenAPI emitter Release build passed with two offline `NU1900` warnings. The Angular development server compiled; the built-in browser showed the staff sign-in guard because the local API/session host was unavailable, so authenticated queue rendering was not exercised. Angular production build previously aborted with exit 134 and no compiler diagnostics. Narrative metrics, UI inventory and diff checks passed; Markdown health and filename checks flag only the separately staged, untouched user file `STE_Audit_Management_Requirements_v2.2.md`. The migration was not applied, full Domain/API/E2E suites were not run, and no live, production or professional acceptance is claimed. The v2.2 workflow epic remains PARTIAL.

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

## Current verification handoff — 2026-10-10

Legal/FSLI implementation and current-tree STE journeys passed local verification. See the [STE verification tracker](auditsphere-execution-tracker-ste-v21-gap-verification-current.md) and `status.json`. T003 professional sign-off, T014 dependencies and live Microsoft/provider acceptance remain open.

The earlier 2026-10-10 M365 portal setup trace is preserved in the [historical current-slice archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md); the later exact-site verification below supersedes its setup status.

## AS-PAR-009 commercial pricing, revocation and delivery receipts — 2026-10-10

Committed at `cee546f9`. Pricing policy maker/checker, append-only approval revocation, quotation validity, signatory-bound acceptance and durable mail receipt states are implemented with migration `20261010185946_CommercialPricingPolicyAndDeliveryReceipts`. Observed: Release build, EF drift check, OpenAPI check, the targeted commercial Domain subset and the Angular build and specs passed. The full Domain, API and E2E suites were not run on this tree. Exact counts and open items are in `status.json` under AS-PAR-009.

## STE-NXT-014 end-of-service provision accrual and report certificate signature evidence — 2026-10-10

Working-tree slice on `cee546f9`. The ADR-0010 recording mechanism is implemented: a recorded and Partner-confirmed accounting treatment, manual accrual journals with an entered basis through the journal maker and checker, and a posting gate in Application and in the database. Report certificate signature evidence is added with migration `20261010203413_ReportCertificateSignatureEvidence`, PEM signing credential source, deliverable signature verification endpoint and Angular verification. Observed: Release build, EF drift check, OpenAPI regeneration and Angular build passed. Per user instruction, automated tests were not run locally. No treatment has been confirmed, so end-of-service posting stays blocked. Details are in `status.json` under STE-NXT-014.

## ADR-0016 certificate signing of the final report — 2026-10-10

Working-tree slice on `cee546f9`. On the owner's instruction the final report PDF is certificate-signed when the firm's certificate is configured (`ReportSigning:*`), the bundle's certified statements are signed with it, and `ReportSigning:Required` fails closed. With no certificate the image-only behaviour is unchanged and labelled. Observed for this slice: Release build, EF drift check, OpenAPI check, the Domain test classes `ReportCertificateSigningTests` and `AuditDeliverablesTests`, and the `completion.spec.ts` screen specs passed, using a throwaway self-signed certificate. The full suites were not run. The firm's real certificate, its legal acceptance and the Wiki deployment note remain open; details are in `status.json` under ADR-0016.

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

## Cash-flow source allocations (CP-11, first slice) — 2026-10-14

A cash-flow movement in a financial package can now be fully allocated to accepted general-ledger lines. Each contribution is a persisted row (`financial_package_cash_flow_allocations`, migration `CashFlowSourceAllocations`) carrying the exact source line, its transaction and batch, a digest of that line, and a signed amount. The rows are immutable with the package, enforced by a trigger. Package build resolves every contribution against the engagement's accepted general ledger, the package period, currency and sealed identity. It refuses over-allocated, sign-reversed, repeated or foreign lines and stores nothing for a refused build.

The presentation lineage check reads those contributions. It passes only when every movement, current and comparative, totals its amount exactly; the old hard-coded failure is gone. Preparer text is never lineage. A build records the check only when it receives links, so unlinked drafts behave as before. A partly linked package is review-required.

Not done: no endpoint or screen accepts the supplementary cash-flow input, so the Angular journey (CP-11.AC-06) is not met. Independent review of allocations, the indirect-method decomposition and the assessed-zero FX decision (CP-12) remain. Evidence is in `status.json` under `targetedVerification.cp11CashFlowSourceAllocations`.

## Cash-flow source review and presentation approval (CP-11 completion) — 2026-10-14

Cash-flow source links now need an independent decision before the presentation treats them as lineage. A reviewer with an accounting or management role records `APPROVED` or `RETURNED` (with a reason) against the package. Decisions are append-only (`financial_package_cash_flow_source_reviews`). The preparer who resolved a link can never review it, and a link with no recorded preparer is refused. An approval is valid only while the accepted general ledger it cites is still the current one; replacing the ledger stales it, and the presentation stops reading the links as approved. Lineage also refuses a cash-account line as a movement source, because that is a transfer. The source manifest seals a review only when one exists, so existing identities do not move. The API exposes `GET` and `POST /api/ui/accounting/packages/{id}/cash-flow-source-review`, and the package page has a review panel.

Not done: the supplementary cash-flow input still has no endpoint or screen (CP-11.AC-06 is open), so a source-linked package cannot yet be built through the product. CP-12 (indirect decomposition, payment roles and the reviewed assessed-zero FX decision) is blocked on the approved indirect classification policy. Evidence is in `status.json` under `targetedVerification.cp11SourceReviewAndPresentation`.

<!-- Moved verbatim from the current slice on 2026-10-15 to keep it under its 60 KB limit. -->

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

## v2.2 workflow analytics — procedure review turnaround — 2026-10-12

Practice Analytics now shows average elapsed hours from submission of the exact procedure-result revision to each append-only review decision created in the selected date range. Both `REVIEWED` and `CHANGES_REQUIRED` decisions count. Revision mismatches and decisions timestamped before submission are excluded and counted; an empty valid sample is `n/a`. The query returns aggregate counts only and does not expose reviewer comments or names, change review state, or imply review quality or audit clearance. A `(FirmId, CreatedAt)` index supports the bounded review-period query; migration `20261012052354_WorkflowReviewAnalyticsIndex` is additive and has not been applied to a persistent database.

Verification on the working tree based on `690e1c11`: API Release build passed with 0 errors and two offline `NU1900` warnings; the focused PostgreSQL analytics regression passed 1/1, including both review outcomes, date filtering, invalid chronology and revision mismatch; the Angular analytics contract spec passed 4/4; EF model drift reported no pending changes; OpenAPI drift passed unchanged; and the Angular development server compiled. The built-in browser redirected the analytics route to the staff sign-in guard, so no authenticated data rendering was observed. The Angular production build aborted with exit 134 and no diagnostics. No full suite, live-provider or professional acceptance was run. Exact evidence is in `status.json` under `targetedVerification.workflowReviewTurnaround`.

The phase task rollups are recorded in the newer section above. WF-14 remains partial: client wait, additional append-only activity sources, analytics pagination/performance acceptance and the remaining workflow stories are open. No persistent database was migrated.

## Current-slice sections archived on 2026-10-15

## Dynamic statement note references — 2026-10-11

Statement face-line note references now separate stable persisted note codes from display ordinals. The presentation assigns numbers from each code's first face-line position in the published layout, then uses the same number on each linked face line; reordering supplied note rows does not change numbering. Blank/oversized logical IDs and duplicate case-normalized (ID, face destination) pointers are refused. The presentation manifest builder version is v4. A failed `NOTE_CONTENT` check and warning state that the persisted data contains only cross-cast amounts, without ordered narrative, typed note tables or comparative note values. The output remains `UNAPPROVED_DRAFT`; no note content or accounting policy is invented.

Observed: the focused Domain and Angular presentation tests passed. The API Release build passed with NuGet vulnerability-feed warnings; Markdown health, filename, narrative-metric, UI inventory, status JSON and diff checks passed. The OpenAPI schema shape is unchanged by the note-number value update; its drift gate passed after the preceding presentation slice and was not rerun here. No migration or business-data change was made. FSR-008 remains PARTIAL: disclosure applicability, ordered note content, prior schedules, PPE/expense schedule completeness and seal-time pointer checks are open.

## Cross-statement readiness checks and export diagnostics — 2026-10-11

The statement presentation now cross-casts current and comparative equity rollforwards to mapped profit or loss, OCI and SFP balances. Cash-flow readiness requires operating, investing and financing amounts, validates current and comparative bridges independently, and carries the package's persisted `CASH_FLOW_RECONCILED` check, which also validates the selected closing-cash population. PDF, DOCX and XLSX all include the same per-check `PASS`/`FAIL` diagnostics in the presentation basis. Builder version is v3. Missing comparative schedules and incomplete source data remain explicitly failed or unavailable; output remains `UNAPPROVED_DRAFT`.

Observed: the focused Domain and Angular presentation tests passed; the API Release build passed with NuGet vulnerability-feed warnings; Markdown health, filename, narrative-metric, status JSON and `git diff --check` gates passed. The .NET test host initially could not bind its runner socket in the sandbox; the elevated retry ran and passed the focused suite. No full suite or PostgreSQL-backed integration test was run. A signed-in browser export journey was unavailable; the local UI had previously stopped at its unauthenticated access gate. No migration or business-data change was made. FSR remains PARTIAL; this slice does not provide source-record lineage, professional policy approval, complete notes/disclosures, final composition, sealing, signing acceptance or release.

## v2.2 workflow experience, PRs D–F — 2026-10-12

WF-04 keeps `WorkTask` as the one operational task/status record and binds optional workflow metadata one-to-one. Existing `TimeEntry` lineage remains attached to the same task. The default-off API exposes scoped task create/read/assign/reassign/complete; creation uses a stable idempotency key and immutable digest, and revisioned actions reject stale requests. Checklist completion does not advance the canonical audit lifecycle.

WF-05 adds a firm-scoped template catalog with immutable revisions, dependency-cycle validation, independent review, publication/supersession and archive evidence. WF-06 adds a preview/execute/receipt path that pins the exact published revision, engagement generation, staffing snapshot and statutory schedule basis. Instantiation creates existing `WorkTask` records and bindings in one transaction. Concurrent same-request execution creates one plan; an ambiguous retry recovers its original receipt even after the schedule changes. A new published template version does not mutate a prior engagement plan. Plan receipts are append-only in PostgreSQL.

PR-F partially advances WF-07/08. `WorkflowTaskQueueQuery` returns My Tasks, Team, Overdue and Completed views scoped from current unexpired role grants, with stable keyset paging and task-owner, FSLI and mapping lineage. The Angular `My work` screen is at `/ui/app/work`. Plan execution now records append-only dependency edges only when they match the exact pinned template steps; task completion is refused until each `TASK_COMPLETED` predecessor is complete. These edges remain operational checklist controls and do not set audit review, lifecycle or approval states. The newer Manager Review follow-up above adds a read-only handoff summary and feature-flagged rework task creation. Waiting-for-client, audit-review and rework queue filters, domain-evidence dependency evaluators, Kanban/calendar views and task-plan authoring screens remain open.

Migrations `20261012000128_WorkflowTaskBindings`, `20261012003440_WorkflowTemplateCatalog`, `20261012004124_WorkflowTemplateOpenRevision`, `20261012010500_WorkflowTaskPlanReceipts` and `20261012015133_WorkflowTaskDependencies` are additive and were not applied to any persistent database. The focused queue/plan PostgreSQL cohort passed 3/3; the API-host OpenAPI contract test passed 1/1; the API Release build passed with two NU1900 vulnerability-feed warnings; EF model drift and OpenAPI drift passed. Angular development serving compiled successfully. The built-in browser loaded the app and showed Access unavailable / Sign in because no local API session was configured; no authenticated queue journey was run. Exact provenance is in `status.json` under `targetedVerification.workflowTaskQueuePrF`.

`WorkflowExperience:Enabled` remains false by default; migrations are not applied to a persistent database. The queue route is present, but local browser access stopped at the staff sign-in guard, and no authenticated queue command was exercised. WF-07/08 and WF-12 remain partial. WF-09/10 automations and PBC follow-up, WF-11 annual recurrence, WF-13/14 completion/archive cards and analytics, the lead-level journey, and WF-15 production acceptance remain open. No live tenant or professional acceptance is claimed.

## Exact pinned financial statement drafts — 2026-10-12

The FSR presentation slice now saves an immutable draft revision against the exact package inputs, published layout version/hash, comparative package/hash, note-source manifest, builder/renderer versions and display policy. Repeating the same pin request is idempotent. PDF, DOCX and XLSX bytes are stored as package artifacts and linked to that revision; repeat downloads reuse the stored bytes. A source change makes the pin stale: the historical rendered bytes remain retrievable, while an unrendered format cannot be created from the stale snapshot. The package seal manifest records pinned presentation identity and artifact hashes without changing the draft's `UNAPPROVED_DRAFT` status or treating it as an approved/issued statement. Cross-casts for current and comparative note amounts are checked against their own package balances.

Observed on this uncommitted working tree based on `00376915`: the focused PostgreSQL Domain regression passed 30/30; the Angular presentation spec passed 4/4; the API and OpenAPI emitter Release builds passed; EF model drift reported no pending changes; and the OpenAPI contract was regenerated. Angular production build passed using the SQLite cache backend, with the existing `commercial/settings.scss` budget warning. `status.json` records the focused commands and provenance. No full suite or signed-in browser workflow was run for this slice. No development or production database was migrated and no live financial data was changed.

This remains a partial, unapproved draft capability. The manifest preserves source identities, but preparer-entered references are not independently resolved to ledger evidence. Professional framework/display policy approval, the approved golden fixture, full note narratives and disclosure schedules, review and authorization, audited report assembly, signing/legal acceptance, release/archive and production acceptance remain open. Full cash-flow and equity completeness also remains gated by approved methodology and source lineage.

## AS-PM8 atomic practice-productivity package — 2026-10-16

Tasks 01–25 and the Task 26 Playwright journey are on `codex/as-pm8-atomic` at code commit `6e4d8682fa4886223422990f010f6d40e8729fe6`. All eight focused acceptance rows pass: Domain 56/56, API 14/14, Playwright 1/1, Angular 839/839. Release build, EF drift, OpenAPI drift, Angular build and docs checks pass; Angular retains the existing `commercial/settings.scss` budget warning (7.53/4.00 kB). Full commands and per-row evidence are in `status.json`, `targetedVerification.asPm8Atomic`.

The full solution suite stopped after about 34 minutes in API tests, with two fixture failures and no final count. The fixtures were repaired in `b440bc77`: receivables now seeds the exact rendered offer and immutable response and asserts the unrelated posted invoice remains in the report; consolidation selects the mapping for the chosen package and verifies approval stays blocked until composition is selected. Those two API regressions passed 2/2 in 45 seconds. A later full-suite retry was cancelled after 18 minutes because a separate API test run was active against the same worktree; it produced no final count. PR #41 was merged by the repository owner as `d277e6a5`; `master` is now synced through `b440bc77`. Hosted CI run [38808307291](https://github.com/nirzaf/AuditSphere/actions/runs/38808307291) passed on PR tree `d0aeac96fbdc0a8bf2628700d54c769c82b68338`, including Angular, Docs Health, .NET Release, focused regression/browser journeys, EF/OpenAPI and publication checks. AS-PM8 remains **PARTIAL** until a full regression completes. No PM8 migration, live provider action, deployment or professional decision was made; external and whole-product gates are unchanged.


## 2026-10-15 operational, deployment and migration updates

The acceptance VPS received API image `48e9f205` from checkout `12e51e60` after a restored-backup rehearsal and 34-migration rehearsal; the acceptance database reached migration `20261015113704` (225), only the API was recreated, and the temporary SSH key was removed. Dedicated client sites remain fail-closed because the rollout cutoff is unset and the isolated worker is stopped. CP-19 receipt recovery, group-role revocation fencing (focused Domain 9/9), and Angular migration parity were also recorded. Hosted CI run [38781204290](https://github.com/nirzaf/AuditSphere/actions/runs/38781204290) passed its focused Angular, Domain, API, E2E, EF/OpenAPI, Docs Health and GitGuardian checks; Microsoft provider behavior was simulated and migration parity remains partial. Detailed status is in `status.json`.

## AS-PM8 initial implementation evidence

The original implementation/merge checkpoint is preserved verbatim in the [historical current-slice archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md). Current package and deployment evidence is in `status.json` and the follow-up above.
## Dedicated client SharePoint sites default rollout — 2026-10-12

`ClientSites:Enabled` now defaults to `true` in the API, worker and Hostinger Compose stack, with an explicit false override. The rollout still requires one administrator-approved UTC `ClientSites:ClientsCreatedAfter` boundary; an absent or invalid boundary never becomes the beginning of time. Historical clients before the boundary are not selected or migrated. The `client-sites` worker starts as an isolated group and reports blocked readiness when its configuration, certificate, tenant consent or rollout boundary is unavailable.

The API and `worker-pbc` receive the same cutoff. Repository resolution blocks shared-site folder creation for clients covered by the enabled or already-persisted cutoff until a dedicated site is verified, including while the dedicated-site worker is blocked and before it can persist the cutoff. Invalid/mismatched settings block dedicated-site discovery; a saved boundary remains authoritative, and conflicting saved values use the earliest recorded boundary for no-fallback enforcement while earlier clients retain their repositories. When the policy is enabled but neither a valid configured nor persisted boundary exists, workspace review/provisioning now also fails closed instead of routing an unclassified client to the shared site; setting an explicit cutoff or explicitly disabling the policy is required before shared-site provisioning. A verified dedicated site continues to serve its client. Automatic discovery remains idempotent per client, and the existing approved yearly engagement templates remain in use. Administration now shows the effective policy, cutoff, counts, tenant consent, worker heartbeat, site permission evidence, per-client site and membership states, durable operation diagnostics, and the existing Operations recovery route.

The heartbeat migration is part of the source tree and model snapshot. The latest grouped follow-up on 2026-10-12 passed the focused Domain cohort 37/37, workspace provisioning API tests 4/4, PostgreSQL-backed Playwright journey 1/1 and Angular workspace administration spec 4/4. The Release test builds emitted NU1900 warnings because `api.nuget.org` was unreachable; the tests themselves passed against isolated synthetic fixtures. These results are from an uncommitted working tree based on `00376915`, which also contains separate in-progress financial statement presentation edits; the full Domain, API and E2E suites were not run. Markdown health, filename and narrative-metrics gates passed. A read-only built-in Chrome check on 2026-10-12 loaded the acceptance tenant page and rendered the Dedicated client SharePoint sites panel, with no client-site rows. The page did not identify its deployed source commit, so this does not prove the current source revision is deployed. No development, acceptance or production migration was run; no real client site was provisioned and no staff membership reconciliation was performed. Exact commands and provenance are in [`status.json`](status.json). Live acceptance still requires identifying the deployed revision, applying the approved UTC cutoff consistently to API and worker configuration, and verifying a synthetic new-client site and staff membership reconciliation in the real tenant.


## Archived current-slice detail — 2026-10-17

## v2.2 workflow activity — task event trace — 2026-10-12

WF-14 now persists append-only `Operational Task` activity for workflow-task creation, initial assignment, assignment, reassignment and completion. Each event links the exact existing `WorkTask` and `WorkflowTaskBinding`, scope, actor, optional assigned staff member, task revision and timestamp. Events are written in the same transaction as the successful task command; idempotent replays and refused/no-op commands do not create duplicate events. PostgreSQL rejects event updates and deletes and rejects inserts whose work task does not match the referenced binding. The timeline query reauthorizes the current engagement grant, caps date filters at 90 UTC days, uses keyset pagination and returns no task title, free-form note, risk evidence or audit conclusion from the event table.

The staff work queue now has a per-task activity panel. This increment covers only operational task events and labels them `Operational Task`. Template publication already has its own append-only decision records; template/reminder/automation events are not yet projected into a unified timeline. Reopened/rejected tasks and due-date changes have no activity coverage here. Client-wait metrics, other source labels, broader analytics pagination/performance evidence, and remaining WF-14 stories remain open. Additive migrations `20261012060111_WorkflowTaskActivity` and `20261012061421_WorkflowTaskActivityBindingGuard` have not been applied to a persistent database.

Verification on the working tree based on `445e1d99`: API Release build passed with 0 errors and two offline `NU1900` warnings; the focused PostgreSQL workflow-task activity regression passed 1/1, covering create/assign/reassign/complete, idempotent replay, timeline paging, foreign-scope and revoked-grant denials, database update/delete rejection, and mismatched task/binding insert rejection; the Angular activity contract spec passed 3/3; the development server compiled; EF model drift reported no pending changes; OpenAPI drift passed. The production Angular build aborted with exit 134 and no diagnostics. The built-in browser showed the staff sign-in guard because the local API/session was unavailable, so authenticated activity rendering was not observed. No full suite or persistent database migration was run. Exact evidence is in `status.json` under `targetedVerification.workflowTaskActivity`.


## Operations — 2026-10-16

`e4686058` pushed; release CI run 38946802126 active.

## Audit planning and finding recovery — 2026-10-16

Planning and finding actions use exact previews, assent and immutable receipts. Finding amendments retain before/after history and scoped authority; snapshots return canonical UTC. Record drafts and library reads have scope fences. Period editors retain request identities on scope cleanup; roll-forward/restatement source parity is verified. Batched evidence and open browser/hosted gates: `status.json` targetedVerification.angularFindingRecovery. Retirement remains NOT_READY; Web retained.

## Moved from the active handoff on 2026-10-17

## AS-PM8 acceptance follow-up

The acceptance migration batch closes direct checklist navigation, command/receipt isolation cases and creation validation/review-reset journeys. Focused verification passed; exact attribution and remaining boundaries are in `status.json` under `targetedVerification.angularMigrationAcceptanceBatch`. Retirement remains NOT_READY.

Consolidation fixtures now use genuinely built packages and composition-bound reviews; sparse translation calculations retain separate approval-denial checks. Rollout-boundary and staffing lost-response timing fixes passed their focused cohorts. The interrupted regression runs lost their process handles and temporary reports, so final commit-pinned release verification is required; workspace-local evidence is retained. Exact candidate, completed results and corrections are recorded in `status.json`.

The refreshed complete Angular suite and production build pass; the existing commercial stylesheet warning remains. UI and retirement source inventories match. Acceptance rows now name the specification’s actual atomic task ranges. The interrupted broad runs do not verify the final checkout; final release verification remains required.

Commit-pinned release verification run [38988587433](https://github.com/nirzaf/AuditSphere/actions/runs/38988587433) targets `512696edfe67ef29bfa2323c3bf4f226b0c13999`: Angular passed 1,288/1,288, Domain passed 1,328/1,328, and build/contracts/publication passed. The API runner received a shutdown signal and exited 143 before uploading test results; that is infrastructure interruption, not a test pass or assertion failure. As supplementary current-source evidence, the six AS-PM8 API classes passed 14/14 locally against isolated loopback `auditsphere_tests`; this does not replace the unfiltered API job. The full Playwright E2E job is still running. AS-PM8 remains PARTIAL until API passes unfiltered CI and E2E completes successfully. Current evidence is also recorded under `targetedVerification.asPm8Atomic.latestReleaseVerification` in `status.json`.

Refreshed Domain 56/56 and API 18/18 at candidate `6944ab95`, including dashboard and UTC start-minute endpoints omitted from the earlier API filter. Evidence is in `status.json`. Full E2E completed: 247 passed, 2 failed (supplier invoice and budget preparation). Their strengthened test follow-up passed 3/3 at `a24af13d`; a full rerun remains required. Full-solution verification remains incomplete after interrupted local runs. AS-PM8 is PARTIAL. Whole-product production acceptance is not established by this package. Acceptance release `6944ab95` is healthy; migration/startup rehearsals and live timer/dashboard checks passed. Temporary SSH access was removed. See the deployment record in `status.json`.

## Workflow analytics and agent capability tooling — 2026-10-12

The bounded capability runner, task/event queues, workflow analytics (including client wait, phase rollups and staff workload) and annual workflow handoffs were implemented in the 2026-10-12 slices. Historical verification, limitations and still-open WF-14/annual acceptance items are retained under `targetedVerification.agentCapabilityRunner`, `workflowAnalyticsPaging`, `workflowReviewInbox`, `workflowAnnualChecklistHandoff`, `workflowAnnualReferenceMetadata`, `workflowAnnualTemplateReference`, `workflowTaskActivity`, `workflowPhaseRollup` and `workflowExperiencePrH` in [status.json](status.json). These operational measures do not satisfy professional audit gates; the full workflows remain PARTIAL.

## Explicit FX cash-effect assessment — 2026-10-17

Financial-package builds can now record an explicit `ASSESSED_ZERO` or `EFFECTS_RECORDED` state with evidence and rationale. Assessment is unset by default; missing data is not treated as zero. Zero-valued effect rows are refused, assessment and effect rows are bound by a deterministic digest, and independent cash-flow review includes that exact evidence and population. Statement completeness and presentation pins require matching assessment and current review. The additive migration is `20261017080751_FinancialPackageFxAssessment`; it has not been applied to a persistent database.

Focused verification on the working tree based on `ad2f0bd`: Release solution build passed with 0 warnings/errors; the focused PostgreSQL FX/review cohort passed 3/3; affected Angular specs passed 46/46; Angular production build passed with the existing `commercial/settings.scss` budget warning; EF model drift and OpenAPI drift passed. The built-in browser reached the staff sign-in guard on the local Angular route; `/api/ui/session` was unavailable, so authenticated builder interaction was not observed. Exact-head full release CI run `39015225622` is still running; its unfiltered Domain job failed 19 of 1,333 tests on the prior commit, including statement-pin and accounting fixture failures outside this working-tree change. Full suite and live/provider verification remain deferred to CI and external acceptance.

This closes only the technical explicit-assessment/evidence layer. Typed indirect cash-flow decomposition, payment-role classification and professional approval of the indirect policy remain open under CP-12/T003; the reporting backlog remains PARTIAL.

## v2.2 final-release evidence projection — 2026-10-12

The engagement completion query now projects the latest release candidate together with its exact external checkpoint and persisted release row. The view reports candidate and target revisions, manifest SHA-256, checkpoint identity/state/verification time, whether the checkpoint revision and read-back digest match the candidate, and whether the persisted Release identity, checkpoint, target revision and manifest agree. It labels a missing issue `NOT_ISSUED`, a fully matching persisted issue `ISSUED`, and inconsistent state `RECONCILIATION_REQUIRED`. The Angular completion screen displays these distinctions and explicitly says that a candidate is not a release. This is read-only; it adds no release or approval command and no migration.

Verification on the working tree based on `87df1a24`: Release solution build passed with 0 errors and six offline `NU1900` vulnerability-feed warnings; the completion Angular contract spec passed 12/12; the focused PostgreSQL projection test passed 1/1; OpenAPI drift passed with no contract change. The Angular development server compiled the routes, but the built-in browser reached the staff sign-in guard because no local API session was available. The Angular production build aborted with exit 134 and no diagnostics. No full suite, persistent migration, signed-in browser workflow, live provider check or professional acceptance was run. Run results are recorded under `targetedVerification.workflowExperiencePrH` in `status.json`.

This only improves visibility of locally persisted release evidence. Issuance still requires the current approval and verified external checkpoint read-back; live delivery and SharePoint/provider acceptance remain external. Overall WF-13 and v2.2 acceptance remain partial.

## v2.2 Manager Review and rework follow-up — 2026-10-12

`ManagerReviewSummaryQuery` adds a read-only, scope-authorized summary over the existing fieldwork completion evaluation. It reports redacted blockers, procedure and evidence counts, unresolved threads, open confirmations, reconciliation/difference work, and whether the latest Summary Review Memorandum is current. `PartnerHandoffReady` requires both current completion readiness and a current SRM; this projection does not record a review decision, opinion, or lifecycle transition. `GET /api/ui/engagements/{id}/manager-review` serves the summary, and the Angular fieldwork page shows it only to actors who can review selections.

When `WorkflowExperience:Enabled` is true, an authorized `ChangesRequired` review creates one idempotently keyed `WorkTask` and workflow binding in the same transaction as the append-only review decision. The task is tied to the exact procedure/result revision and goes to the prior preparer only while that user remains eligible; otherwise it is unassigned for manager routing. No notification is sent. No migration was added for this follow-up.

The two focused PostgreSQL regression tests passed 2/2, including the returned-work blocker, stable redaction, linked task, prior-preparer assignment and duplicate-review refusal. The API Release build, Angular production build, EF model-drift check and OpenAPI contract drift check passed; the .NET build emitted two `NU1900` vulnerability-feed warnings because NuGet was unreachable. The built-in browser could not load the local fieldwork route because the local Angular server was no longer listening; the existing local tab reached only the staff sign-in guard. No authenticated browser journey or full suite was run. Exact commands and results are in `status.json` under `targetedVerification.workflowManagerReviewPrF`.

This is a partial workflow increment. The canonical lifecycle stage does not regress to Fieldwork on rework; the summary flag does not change lifecycle gates; procedure-level assignment/resubmission restrictions and task notifications remain open. Queue filters for rework and waiting-for-client, evidence dependency evaluators, Kanban/calendar views, and task-plan authoring remain open. The workflow feature stays default-off; no live or professional acceptance is claimed.

## Annual preparation snapshots moved from the compact handoff

## Reviewed annual template reference — 2026-10-13

Annual preparation suggests the current published revision of a prior plan’s template. The optional choice is retained in the exact creation preview and immutable receipt, then preselected in the new engagement’s task-plan screen only while current. An archived/superseded reference blocks new creation; the plan still requires separate staffing/schedule review and assent. Prior plans are unchanged. A CI-discovered assessment receipt timestamp mismatch is corrected by returning the persisted row on first publication. Evidence is under `targetedVerification.workflowAnnualTemplateReference` in [status.json](status.json). Nonstandard calendars, populated-mapping browser acceptance and wider workflow/release gates remain open.

## Annual checklist handoff — 2026-10-13

Annual receipts now open the exact new engagement in the existing client assessment. The query confirms client ownership, shows whether a fresh evaluation is needed, and retains parent-client authorization. Opening a continuance review still requires reviewed assent; delta answers start empty and the Partner decision editor fixes the exact engagement and service. Period/revision changes invalidate scoped decision previews under a shared engagement lock. Prior decisions and answers remain history; activation stays separate. Evidence is under `targetedVerification.workflowAnnualChecklistHandoff` in [status.json](status.json). Template preselection, populated-mapping browser acceptance, nonstandard calendars and broader acceptance remain open.

## Annual reference metadata — 2026-10-13

Annual preparation suggests active client contacts valid at the next-period start and approved mappings from the exact prior engagement and period. Accounting metadata requires source-engagement accounting read authority; RelationshipManagers receive a restricted state. References are bounded and never copy allocations, routing, access, consent or approvals. Evidence is under `targetedVerification.workflowAnnualReferenceMetadata` in [status.json](status.json). Fresh-checklist orchestration and broader acceptance remain open.

## Agent capability evidence and pilot comparison — 2026-10-13

The existing [capability runner](../testing/auditsphere-testing-guide-agent-capability-checks-current.md) maps acceptance to named checks, exposes synthetic actors/states and omitted evidence, parses sanitized assertion/request-ID context, and detects HTML/SCSS/new-file changes. Explicit browser execution builds the current UI first; reports cannot overwrite source or prior evidence. Matching-cohort comparisons retain missing measurements instead of inventing gains. Focused UI/browser, authorization/API and calculator pilots passed; source hashes, the resolved socket-denial attempt and limitations are under `targetedVerification.agentCapabilityRunner` in [status.json](status.json). Comparative productivity measurements, broader release checks and external/professional acceptance remain open.

Earlier workflow analytics, FX and release/rework increment notes are retained verbatim in the [historical current-slice archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md).


## Live domain evidence prerequisites — 2026-10-13

WF-08 task prerequisites now reference current independently approved materiality, an exact accepted/closed PBC request or a current independently reviewed procedure. Satisfaction is evaluated live; no editable satisfaction flag exists. Scoped managers review revision-fenced changes with reasons and recover uncertain replies by reading their actor-owned receipt. Append-only policy history and exact source foreign keys are database-enforced; both workflow and legacy task completion honor required evidence. PBC waits feed the client-wait queue; stale procedure generations and open review notes remain blockers. Expired role grants no longer elevate task completion.

The focused .NET, Angular and owned browser batch passed after correcting synthetic fixture/assertion issues. Built-in-browser inspection also confirmed a reviewed withdrawal changes only operational policy and retains the unsatisfied professional source. Exact counts, source digest, artifacts and migration evidence are in `targetedVerification.workflowDomainEvidencePrerequisites` in [status.json](status.json). No persistent database was migrated. WF-08 remains partial: template-declared domain rules and safe manual task-edge amendments are next; broader workflow, release and external/professional acceptance remain open.

## Reviewed task dependency amendments — 2026-10-13

WF-08 now supports manager-reviewed required/withdrawn task edges as append-only amendments over the original pinned template graph. Both task revisions, exact engagement scope and a reason are bound to the actor-owned idempotency receipt. The complete effective graph is serialized and checked for cycles, including opposite-edge races; PostgreSQL also rejects raw cyclic inserts and history mutation. Queue readiness and both completion routes use the same effective graph. No professional approval or pinned template row changes.

The Angular ordering panel provides exact review and assent, receipt-only recovery for lost replies, explicit dropdown labels and session/scope clearing. The focused Domain/API/Angular/browser batch passed after correcting analyzer assertions and a label lookup issue. Built-in-browser inspection confirmed a reviewed withdrawal appends history and restores only the operational action. Exact counts, source hash, artifacts and limitations are under `targetedVerification.workflowTaskDependencyAmendments` in [status.json](status.json). No persistent database was migrated. Template-declared domain rules, other workflow gaps and full release/external/professional acceptance remain open.

## Archived current-slice sections moved on 2026-10-18

The following older dated sections were moved verbatim from the compact current-slice handoff to keep its active portion within the documentation-size limit. They are historical evidence; `status.json` and current authority documents govern present state.

## Client portal invoice settlement projection — implementation packet (2026-10-18)

**Implementation commit:** `de8b6655d250867262550e998cd6d5fe46388977`, pushed to `master`.

- **Task and requirement:** implement the buildable projection slice of ASPM-COM-06 / COM-06 in the proposed complete-practice-management specification. The criterion requires distinct invoice lifecycle, posting/sending, cash collection, due/overdue and provider-delivery truth.
- **Authority and existing behavior:** the portal currently uses `ClientPortalFinanceQuery` and exact client/engagement grants; `BillingStates`, append-only `InvoicePaymentTermsRevision`, `FirmReceivablesAgingPolicy` and `FirmReceivablesAgingQuery` define invoice lifecycle, recorded cash, approved reversals, issued credits, effective due date and UTC-date aging. The criterion audit at ASPM-COM-06 confirms exact grant scope but identifies the missing state projection. Existing provider rules distinguish provider acceptance from delivery.
- **Delta:** expose actual `PostedAt` and `SentAt`, net recorded cash after approved allocation reversals, issued credit, outstanding amount, the latest effective independently approved due date and shared aging bucket. Do not present a draft/unposted/cancelled invoice as paid or overdue. Invoice-specific provider delivery is not currently linked to the durable mail record, so report it as untracked; do not infer it from `SENT`, `PROVIDER_ACCEPTED`, or receipt-mail state. Include the UTC as-of date used for aging.
- **Boundaries:** no invoice send, payment, credit, accounting, provider, configuration or authorization mutation; preserve exact client and engagement grants, existing audit 50/50 terms, immutable invoice/receipt/reversal history, and all unrelated firm-finance data. Preserve lifecycle status separately from collection and aging status; a credit-settled invoice is not cash-paid.
- **Code owners:** `Application/Documents/ClientPortalFinanceQuery.cs`, `Ui/src/app/features/portal/finance.ts`, generated OpenAPI contract, the existing `CommercialWorkflowTests` portal finance scenario and `home.spec.ts`; update the PM criterion audit and active handoff only with observed evidence.
- **Focused acceptance:** PostgreSQL behavior check for full cash payment, reversal-reopened partial amount, approved due date/overdue bucket, draft/unposted and delivery-untracked states while retaining sibling-scope denial; one focused Angular portal-finance spec validates decoder and truthful lifecycle/collection/aging labels. No full local suite; no live-provider acceptance inferred.
- **Gates:** full exact-SHA CI remains the complete regression gate. Browser-visible behavior must be exercised in the built-in browser where a current local host can be started without disturbing the user's server or business data. Provider delivery proof and broader COM-06 acceptance remain external/incomplete until an invoice-specific provider receipt exists.

**Observed result:** OpenAPI emitter Release build passed with 0 warnings/errors; OpenAPI drift check passed. The focused PostgreSQL Domain regression `PortalFinanceAndReceiptDownloads_RespectExactClientAndEngagementScope` passed 1/1: a real draft invoice remains `NOT_POSTED` with no posted/sent dates, no outstanding receivable or aging bucket; the posted invoice is cash-paid; an independently approved due date is retained; and an approved QAR 100 reversal reopens it as partially paid and 1–30 days overdue. The focused Angular `home.spec.ts` passed 7/7, including separate lifecycle/cash/credit/aging/delivery columns and the UTC as-of date. Angular production build passed with the existing `commercial/settings.scss` budget warning (7.53 kB against 4.00 kB). The built-in browser opened the existing local host at port 5029; `/portal` redirected the signed-in staff account to the firm workspace, so client-authorized invoice data and browser console/network acceptance were not established. No business records were changed. ASPM-COM-06 remains PARTIAL; full current-SHA CI and an invoice-specific delivery receipt are outstanding.

## AS-AWP-EXEC Atomic Task 03 — immutable effective procedure definitions — 2026-10-17

Controlled procedure tailoring now appends an immutable effective-definition revision using the expected current revision and command identity. The original wording remains resolved from its exact adopted catalogue row. Existing tailored rows receive explicitly unattributed legacy snapshots; their migration timestamp is not presented as an authoring time. New results retain the exact definition revision used, and review/current-evidence gates reject results bound to older or missing definitions without changing their submitted evidence or incrementing the result sequence during tailoring. The fieldwork projection presents original wording, effective instructions and provenance, including an explicit label for legacy snapshots.

Focused verification: Domain PostgreSQL regression 1/1 (43 seconds); API fieldwork projection/adoption regression 1/1 (44 seconds); Angular fieldwork spec 12/12; EF reports no pending model changes; OpenAPI contract regeneration and drift check pass. The migration was exercised through the isolated test schema and not applied to a persistent database. Documentation health, filename, narrative-metric, UI-inventory and whitespace/JSON checks pass. The change is committed and pushed as `3ae06950`; CI run 39035083051 is queued. No authenticated local browser fixture was available; browser journey is not run. Tasks 04–11, full-suite CI, professional methodology acceptance and live-provider/production acceptance remain open. Exact results are in [`status.json`](status.json#targetedVerification.atomicAuditWorkingProcessTask03).

## AS-AWP-EXEC Atomic Task 01 — controlled audit-program publication — 2026-10-17

Implemented the first dependency-ready task from the owner-authorized audit working-process specification. The fixed 20-section/165-procedure catalogue retains its supplied source wording and SHA-256. Preparation now requires current firm-wide Partner/Administrator authority and creates an immutable DRAFT with a deterministic digest over the ordered procedure identities, section metadata, ordinals and exact wording. Publication is a separate action requiring a distinct currently authorized firm-wide reviewer, the exact digest and a bounded review reference; same-intent replays return the retained version, and changed/legacy content fails closed. Engagement adoption now only adopts an already-published version and cannot publish implicitly. Legacy publication records remain intact and are not assigned fabricated independent approval evidence. Professional methodology approval is still a separate gate.

The Angular library displays provenance and supports explicit prepare/review-publish actions. An additive migration stores content digest and review reference, and its downgrade refuses to erase retained review evidence. The original Word input and independent JSON source manifest are preserved for source-to-catalogue comparison.

Verification on the implementation tree: API Release build passed (0 warnings/errors); focused API publication endpoint test 1/1; focused Domain source-manifest/publication tests 2/2; the touched Domain scope-isolation regression passed 1/1 after correcting a test helper that had widened its synthetic actor; Angular library spec 17/17; E2E test project compilation passed (0 warnings/errors); EF reports no pending model changes; `git diff --check` passed. The initial Domain slice exposed the helper scope issue and that specific failing case was rerun after correction. The migration is generated and not applied to any persistent database. No full suite, authenticated built-in-browser journey, hosted CI, live tenant, or deployment was run. Tasks 02–11 and professional/external acceptance remain open. Exact run data is recorded in `status.json` under `targetedVerification.atomicAuditWorkingProcessTask01`.

`2873144a` adds scoped correction-journal revision links; its PostgreSQL test passed 1/1, with build, Angular 5/5, EF and OpenAPI checks passing. Migration is generated, not applied. `ffa9bd99` makes cash-flow review availability use current scoped authorization; `SourceReviewCanReview_RequiresCurrentScopedReviewGrant` passed 1/1. T025/T026 remain BLOCKED/PARTIAL on T024/T025. Indirect-method policy and source-provenance work remain open; full suites run in CI/CD.

## CP-12 indirect cash-flow calculation roles — 2026-10-17

Commit `1ff7bf48` adds typed calculation roles for starting profit, noncash profit adjustments, operating balance movements, investing/financing cash movements, and disclosure-only noncash investing/financing. Roles are persisted in an additive generated migration, included in calculation/source hashes, validated against section and noncash-source flags, and rendered in the draft schedule. An explicitly classified schedule must classify every row; mixed explicit and legacy interpretation is refused. Noncash profit adjustments enter the operating reconciliation; disclosure-only items do not enter the cash bridge. UI copy states the roles are not professional policy approval.

Observed: `CashFlowContractTests.TypedNonCashProfitAddback` passed 1/1 against PostgreSQL after the mixed-schedule guard; `FinancialStatementPresentationTests.IndirectCashFlowRoles_IncludeNoncashProfitAddbacksButExcludeNoncashDisclosure` passed 1/1; `financial-package-build.spec.ts` passed 37/37; current API Release build passed with 0 warnings/errors; EF reports no pending model changes; Angular production build passed with the existing `commercial/settings.scss` budget warning. Migration `20261017110415_FinancialPackageCashFlowCalculationRoles` is generated and not applied. The built-in browser route refused connection, so no authenticated browser journey was verified.

This is partial technical support, not completion of T030 or CP-12. T029 canonical statement sets and T003's qualified indirect-method policy/independent review remain hard blockers; payment-role taxonomy, all source-backed movement paths, T033 review integration, full suites, and professional/live acceptance remain open. Evidence is in [`status.json`](status.json#targetedVerification.cp12IndirectCashFlowRoles). T030 is recorded BLOCKED pending those prerequisites.

## CP-19 Partner authority and capability projection — 2026-10-17

Partner decisions and release eligibility require the current assigned Engagement Partner and its linked exact engagement-scoped Partner grant. The review queue and financial-package workspace now use the same check before projecting Partner approval as available. The focused PostgreSQL regression passed, including assigned/unassigned positive and negative projections; broader CP-19 signatory, offline-evidence and recovery requirements remain open. Full suites are deferred to CI/CD.

## T028 comparative-source increment — 2026-10-17

Build choices list at most 100 validated same-client comparatives after independent prior-engagement authorization. Cross-engagement builds require compatible linked periods/books and recheck both grants, context and comparative revision/hash under the client lock; snapshot identity enters the package hash. Pinned presentation/artifact reads, candidate promotion, formal review and release checks reauthorize the comparative grant. Angular labels the source engagement and basis.

Two focused PostgreSQL regressions passed (option authorization/revocation and snapshot hash); docs validators passed. Full suites and browser verification were not run locally. Comparative policy, as-issued/restated selection and professional acceptance remain open. T028 is PARTIAL and depends on T027.

> **Archive:** earlier slice sections, moved verbatim on 2026-10-08 and 2026-10-15, are in [`auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md`](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md). They are history, not current authority; the ledger is [`status.json`](status.json).

## CP-10 — period-scoped package freshness — 2026-10-17

Commit `b6e465bc` added same-period TB freshness checks for package pins and presentation. Follow-up `15ae8407` scopes TB acceptance/completeness selection by period, book, basis, currency and entity. The focused PostgreSQL test `TrialBalanceAcceptance_SelectionRemainsIndependentByPeriodAndBook` passed 1/1, covering independent period selections, same-scope replacement and denied reads; the Angular source-acceptance spec passed 5/5. Earlier two-period tests cover package values, prior pin identity, and current-period GL/TB replacement. CP-10 remains PARTIAL: comparative release/as-issued retention, restatement, opening continuity, cross-engagement denial and professional acceptance remain open. Full suites are deferred to CI/CD. Evidence: `targetedVerification.cp10PeriodScopedPackageFreshness` in `status.json`.

### Opening bridge close gate — 2026-10-17

`ClosePeriodAsync` now requires the linked bridge to be approved. `PeriodRollForward_CopiesDraftBooksAndRequiresOpeningEvidence` passed 1/1: close refused before approval and succeeded after. CP-10 remains PARTIAL; comparative release, restatement, broader continuity and professional acceptance remain open. Full suites stay in CI/CD.
Evidence: `targetedVerification.cp10OpeningBridgeCloseGate` in `status.json`.

## CP-13 — signed equity component transfers — 2026-10-17

Commit `8ea17226cd2caaad973730c42a2232dc00dcdfa7` adds paired, append-only transfers between distinct equity components to the financial-package build, with server validation, hash binding, component rollforwards, linked presentation/export rows, archive evidence and Angular input. The migration is generated but not applied. Focused Domain tests passed (24/24 before the final label assertion edit; final presentation test 1/1 after it); the Angular builder spec passed 31/31, production build passed with the existing commercial-settings style-budget warning, and EF model drift is clean. No authenticated browser record flow or full suite was run. CP-13 remains PARTIAL: accepted-source lineage, professional reserve/profit/distribution policy, opening/no-movement cases and professional acceptance are still open. Exact evidence is in `targetedVerification.cp13EquityComponentTransfers` in `status.json`.

## Angular accounting maintenance recovery

Roll-forward, restatement submission and independent approval now share exact previews and append-only receipts. Request recovery retains only identity and hash; missing lookup never silently clears uncertainty. Publication locks and final authorization checks protect client/session boundaries. The approval preview exposes the submitted package pair and evidence. Period-record handoffs are reauthorized after projection. Batched evidence is under `targetedVerification.angularAccountingMaintenanceRecovery` in `status.json`; the migration is generated, not deployed. Accounting parity and Blazor retirement remain incomplete.

## AS-PM8 acceptance follow-up
Detailed dated evidence moved verbatim to [`auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md`](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md). Current AS-PM8 readiness and remaining gates are tracked in `status.json`.

## Angular/Blazor parity — receivables exact-offer fixture

The receivables aging fixture now supplies the exact rendered quotation, dispatch identity and immutable client-response evidence required by the fee-agreement command. Its original as-of-date receipt, credit and allocation assertions remain intact and the focused test passed. The build completed after concurrent-output copy retries. Exact source, command, report and caveats are in `status.json` under `targetedVerification.angularBlazorPackageGateFixtureFollowUp.receivablesFollowUp`.

The full Domain suite has not been rerun after this repair. Consolidation/foreign-operation fixtures still need composition-bound approvals, and source-action, browser, operational and external retirement criteria remain open. This checkpoint predates the 2026-10-17 retirement; see the reading note.

## Angular/Blazor parity — package close/release fixtures and consolidated regression

The package close and release fixtures now render/select a real three-format composition and record independent decisions on that identity. They retain prerequisite refusals; release now asserts the exact composition and PDF pins rather than fabricating package PDF bytes. The focused cases passed, but the consolidated Domain suite failed. Exact source, totals, named failures and reports are in `status.json` under `targetedVerification.angularBlazorPackageGateFixtureFollowUp`.

Remaining failures concern receivables exact-offer response evidence and consolidation/foreign-operation fixture approvals that still omit selected compositions. The required approval guard remains enabled. Migration documents were temporarily removed and restored by another process during the run; no deletions were made or committed here. Retirement remains `NOT_READY`; other source-action, browser, operational and external acceptance is incomplete.

## Angular/Blazor parity — restatement issuance fixture

The restatement regression fixture now uses a presentable source-backed original package, renders and selects its three-format composition, records management/accounting/Partner decisions, and closes the period through the Application command before preparing the correction. It explicitly checks that an unreviewed close is denied and retains the change-type, affected-period and independent approval assertions. The accounting-record approval class passed; exact source, command and report are in `status-archive-2026-10.json` under `keys.targetedVerification.angularBlazorTenantConsentFixtureFollowUp.restatementFollowUp`.

This repairs a local regression fixture without changing production gates. Other full-regression failures and source-action, browser, operational and external retirement criteria remain open. This checkpoint predates the 2026-10-17 retirement; see the reading note.

## Angular/Blazor parity — directory-binding regression fixture

The tenant-consent test fixture now supplies the firm safety row required by role administration's transaction lock and authority recheck. This repairs the exact directory-binding and stale-observation regression cases without changing production authorization or Microsoft capability behavior. The complete tenant-consent class passed; source attribution, command and report are in `status-archive-2026-10.json` under `keys.targetedVerification.angularBlazorTenantConsentFixtureFollowUp`.

A current baseline also reproduced the restatement failure at period close. That fixture records legacy package decisions without selecting a reviewed composition. Its reporting setup needs repair while preserving both the close gate and the positive restatement assertions. Other regression failures and per-action migration evidence remain open. This checkpoint predates the 2026-10-17 retirement; no real tenant acceptance is claimed from these synthetic tests.

## Angular/Blazor parity — consolidation approval and review reconciliation

The consolidation component approval command again requires current management, accounting and Partner decisions bound to the selected composition. A local regression had shown approval succeeding without that composition after the guard was relaxed. The focused API refusal case now passes; exact source attribution and the affected cohort results are in `status.json` under `targetedVerification.angularBlazorConsolidationGuardFollowUp`.

The consolidation source review also contained two stale gaps. The current Angular overview already labels approval times in UTC, including offset normalization. Advanced consolidation already fences unknown outcomes until a persisted workspace re-read and explicit acknowledgement. Existing focused Angular specs passed for these behaviors. The review and source-action manifest now retain the remaining scope, mutation, browser, performance and human acceptance gaps.

This checkpoint predates the 2026-10-17 retirement. The earlier full local regression reported failures and is not current repaired-tree acceptance. No deployment, persistent database migration or external acceptance was performed for this increment.

## Blazor parity — focused security and denial boundaries — 2026-10-15

Focused browser coverage compares foreign-firm and guessed-ID denials for assessment, engagement creation and activation, and verifies the server-owned access-denial page returns 403 with `no-store`, generic text and sign-out. The role-administration PostgreSQL cohort also proves a revoked Administrator waiting on the firm lock cannot finish a grant revocation. These close only narrow boundaries; the full Release solution baseline was interrupted without a verdict. Overall parity remains partial and Blazor removal is NOT_READY. Evidence for these focused checks is preserved in `status-archive-2026-10.json` under `keys.targetedVerification.angularMigrationAcceptanceCrossFirmIsolation`, `angularMigrationAccessNotAssignedBrowser` and `angularMigrationAdminRevocationFencing`; `status.json` retains PASS pointers and PARTIAL scope labels.

## AS-UX-001 Angular visual adoption — shell foundation — 2026-10-15

The existing Angular shell now uses the approved dark navigation/light workspace visual direction. The staff-only global search remains backed by its existing authorized API; its results are presented in a bounded header panel. Existing route groups and session/sign-out behavior remain. The seven visible workflow phases remain a read-only server projection of the canonical lifecycle.

The first slice centralizes shell tokens, adds compact desktop navigation, restyles the responsive seven-phase grid, and ensures the root host fills narrow viewports. No API, domain, route, database, dependency, permission or provider behavior changed.

The local browser preview is unauthenticated. Shell widths were checked without horizontal overflow, but staff navigation, global-search results, engagement context, authenticated ribbon states and feature-screen parity were not observed. AS-UX-001 remains PARTIAL; detailed counts and browser measurements belong in `status.json`.

## STE v2.2 proposal response provenance increment — 2026-10-15

Source: the STE v2.2 end-to-end audit and completion story. Code is on `master` at `9a2630f628ca1c64ebffe576e716cbdc1b849f2e` (pushed). This increment closes the exact rendered-offer and immutable client-response evidence path within CP-03/F-10; it does not close the wider work packages or the whole story.

- Proposal dispatch requires the current approved quotation and exact current rendered artifact with a matching digest. The linked client’s eligible commercial contact is snapshotted; a prospect’s primary contact is captured for the proposal intent. Stale, missing or incomplete legacy offer evidence fails closed.
- Acceptance evidence records the exact offer digest, recipient and route/contact identity, respondent and authority basis, response channel, evidence reference, actor and time. Response evidence is append-only. Exact replay is idempotent; altered replay is refused. Client conversion and fee-agreement progression require the current evidence.
- The durable mail operation attaches the rendered quotation bytes whose digest was checked. The Angular response flow collects the evidence needed to record acceptance or decline.
- The additive persistence migration is generated and model-checked; it has not been applied to any persistent database. No live mail delivery, VPS update, production migration or provider acceptance is claimed.

Verification at the code commit: Release solution build (0 warnings, 0 errors); focused PostgreSQL Domain cohort 42/42; API journey 1/1; Playwright commercial journey 1/1; Angular CI specs 752/752; Angular production build; EF model drift; OpenAPI drift. Angular reports the existing `commercial/settings.scss` style-budget warning. Documentation health still required a stale baseline-reference correction, recorded in the follow-up commit. Regression follow-up at `9a2630f6`: PostgreSQL Domain 148/148 and API 8/8 passed locally; hosted CI run 38724299753 passed all jobs, including 15/15 API-hosted Playwright journeys, Angular, EF/OpenAPI/Docs and publication checks. The private acceptance image build also passed; no deployment occurred. Unfiltered release workflow 38726773300 is running on this SHA.

Remaining CP-03/F-10 work includes full proposal identity/content coverage, all approved recipient-purpose routes, ambiguity/expiry preview and remediation, pricing revocation and unknown-send recovery, truthful provider-delivery evidence, broader regression cohorts and professional/live-provider acceptance. See the specific update and evidence in the [production-readiness audit](auditsphere-execution-audit-production-readiness-gap-closure-current.md) and `status.json` under `targetedVerification.ste22F08CommercialResponse`.

## STE v2.2 end-to-end story — historical handoff

The 2026-10-15 first-increment narrative is preserved in the [historical handoff archive](auditsphere-execution-handoff-history-2026-10-historical.md). Use the current [status ledger](status.json) and [task board](auditsphere-execution-index-task-board-current.md) for open criteria and current evidence.

## F-07 lifecycle phase projection — 2026-10-15

Implemented and focused-verified on master commit `25d11dac` (pulled base `ef42caf9`):

- Procedure progression uses the current result revision and its matching review. A current `CHANGES_REQUIRED` result returns the canonical projection to Fieldwork; a prior returned revision does not control a newer submission. Approved not-applicable procedures count as resolved without a fabricated result. An applicable submission without its review remains in Managerial Review.
- Partner Approval eligibility is based on a current SRM clearance and a clear completion gate; selecting an opinion is an action in that phase, not a prerequisite to seeing it. The focused predicate regression covers readiness before an opinion exists. A fully current, completion-ready SRM fixture for the integrated phase transition remains to be added.
- The report-signature deadline remains visible while the user-facing phase is Report & Delivery. Stage 10 requires a scheduled freeze and a bundle matching the freeze's signed report and the latest financial-package release, including pinned artifact/composition identities and valid stored content/manifest hashes. A committed bundle is available through the client portal; the system does not record that a client opened or downloaded it. An expired clock by itself stays unarchived; stage 11 requires a committed local freeze.
- The 11 canonical states and seven user-facing phases are unchanged. The browser journey checks the ordinary Engagement workflow panel before and after a synthetic early freeze.

Verification: PostgreSQL-backed `EngagementLifecycleQueryTests` passed 5/5 on loopback PostgreSQL 18.6 (`auditsphere_tests`); the focused Playwright browser journey `PartnerReviewsReadinessLocksScheduledFileAndLifecycleKeepsProviderStateSeparate` passed 1/1. The E2E build compiled the API and UI-backed journey. No migration, OpenAPI change, persistent database update, live provider check, or VPS deployment was made. Exact command and remaining evidence gaps are preserved in `status-archive-2026-10.json` under `keys.targetedVerification.ste22F07LifecycleProjection`.

F-07 remains PARTIAL: the positive exact-bundle-to-countdown transition and a fully current SRM/clear-completion Partner phase are not covered by an integrated lifecycle fixture. No provider immutability or live production acceptance is claimed.

## F-08 selected composition through review, release and bundle — 2026-10-15

Implemented on master `7023a9f4`:

- The Angular package screen requires selection of a current sealed composition before it enables a formal decision. Its request includes the exact candidate revision ID, composition ID and lowercase SHA-256 manifest digest.
- Candidate-bound management, accounting and Partner decisions persist those identities. Each later stage must approve the same current composition; release rechecks the Partner decision, source currency and all three artifact hashes, then pins the composition ID/digest and selected PDF identity/version/hash. It refuses to substitute the legacy package PDF.
- The five-part audit bundle requires the released composition, verifies its PDF/DOCX/XLSX bytes, includes those exact files and records their IDs and hashes in the bundle manifest. Legacy releases without a composition remain readable but cannot be replayed as new composed releases.
- Migration `20261015000622_FinancialStatementReviewCompositionBinding` adds nullable, scoped composition references and integrity constraints. EF model drift is clear. The migration was not applied to a persistent local database or the VPS; test fixtures used disposable migrated databases.

Verification: Release solution build passed with 0 warnings and 0 errors. The focused PostgreSQL-backed Domain cohort passed 35/35 (`StatementCandidatePromotionTests`, `FinancialPackageGateTests`, `AuditDeliverablesTests`, and `ClientAccountingTests.FinancialPackages`). Angular `test:ci` passed 127 files / 736 tests. The Angular production build passed with its existing `commercial/settings.scss` budget warning (7.53 kB against 4.00 kB). EF model drift, OpenAPI contract, Markdown health, filename, narrative-metrics and `git diff --check` passed.

The interactive built-in-browser review/release journey was not run: this commit is not deployed to the VPS, and no local authenticated package fixture was started. Full unfiltered Domain, API and E2E suites and the hosted release workflow also remain unrun. F-08 therefore remains PARTIAL: cross-format content/reference parity, approved profile membership, MIME/glyph/clipping validation, management signatory/capacity and offline evidence, review receipt/revision recovery, final audit-report/signing/legal acceptance and live-provider gates remain open. No persistent business data or production deployment was changed.

## Statement presentation signing candidate and formal review artifact pin (CP-18 / CP-19) — 2026-10-15

Implements the governed presentation successor workflow and formal review decision artifact binding under CP-18 and CP-19:
- **Governed draft-to-candidate successor promotion:** Added `PromotePresentationToCandidateAsync` to `FinancialStatementService.PresentationPins.cs` and exposed `POST /accounting/packages/{id}/presentation/pins/{revisionId}/candidate`. Unapproved draft history and bytes remain immutable; candidate promotion validates package validation readiness, independent cash-flow source review, and current source lineage before producing an immutable candidate successor with `PredecessorRevisionId`, candidate banner, and rendered candidate PDF artifact. Repeated promotion of the same draft is idempotent.
- **Formal review decision binding:** `FinancialPackageReviewRequest` and `FinancialPackageReviewDecision` now support `PresentationRevisionId` and target artifact selection. Management, Accounting, and Partner review decisions bind to the exact candidate presentation PDF artifact rather than generic text, verifying that all review stages approve the same candidate presentation and that the candidate's source remains current.
- **Release and deliverable bundle integration:** `ReleaseService` verifies Partner approval and pins the exact reviewed candidate presentation PDF artifact for package release, which `AuditDeliverableService` incorporates directly into the five-part client deliverable bundle.
- **Decoders and contracts:** Angular presentation decoders accept `UNAPPROVED_DRAFT`, `CANDIDATE`, `APPROVED`, and `SUPERSEDED` statuses and decode `predecessorRevisionId`. OpenAPI 3.1 specification regenerated and verified without drift.
- **Verification:** Solution build 0 errors / 0 warnings; EF Core migrations verified with zero model drift; OpenAPI contract verified current; Angular tests passed (127 files, 733 tests); Angular production build passed; 194 focused .NET Domain tests passed; documentation, filename, narrative metrics, and UI inventory validators passed.

## Production-readiness story: cash lineage, decision fence and release pin — 2026-10-15

The [production-readiness audit](auditsphere-execution-audit-production-readiness-gap-closure-current.md) remains **NOT COMPLETE**. Its verdict is unchanged: substantial implementation, incomplete functional acceptance, not production ready. The ledger entry is `targetedVerification.productionReadinessStoryCp11` in `status.json`.

- **Closed on master this session:** the cash-event rule that an unexplained period cash event fails lineage (an FX row can no longer cover it); the declared cash population checked against the approved mapping; decisions ordered by a database identity sequence; share locks that fence acceptance and grant revocation; scoped Partner-stage tests; and a financial-package release that pins the reviewed PDF, which the bundle then uses alone (F-08, release side).
- **Still open:** F-08 composition selection and draft-to-candidate successors (CP-18, CP-19); F-05 to F-07 (professional policy); F-10 and the owner decisions (Administrator's accounting-review rights, CP-02, CP-03, CP-05, CP-22); and the external gates (branch protection, live identity and provider evidence, recovery, capacity).
- **Verified locally:** 245 focused Domain tests, a clean solution build, no EF model drift, and a current OpenAPI contract. **Not run:** the full suites, the E2E journeys and the hosted release workflow.

## Source-linked package build from the product (CP-11 accepted-source cash-flow subpath) — 2026-10-14

Historical implementation narrative: see the [execution handoff history](auditsphere-execution-handoff-history-2026-10-historical.md).

## Accepted GL discovery for schedule preparation — 2026-10-14

The specialist preparation workspace now exposes a bounded view of the selected independently accepted GL for its exact reporting period. Application authorization precedes source discovery; period/currency/sealed identity are checked, source selection and generation are checked again after reading, and signed functional amounts remain exact decimal strings. Source and line identities are visible with paging. Period changes hide prior source content; a mismatched period is refused.

This is CP-11 source discovery, not persisted monetary allocation or independent schedule review. It does not satisfy final cash/equity source-lineage readiness. Indirect cash-flow roles, assessed-zero review and source-backed schedule bindings remain outstanding. The isolated synthetic built-in-browser fixture exercised paging, signed amounts and period clearing/refusal; no live tenant or development database was changed. Exact checks and omissions are in `status.json`.

## Consolidated practice/reporting intake and mandatory-rule readiness — 2026-10-14

The supplied completion story extends the reporting handoff using consolidation aliases over the existing board; it does not approve methodology or create another lifecycle. Its inherited reporting criteria were compared with the preceding source without changes. Current HEAD already contains the Qatar working-week analytics fixture and audit-submission fence corrections; neither was overwritten.

The existing canonical package rule population now has one shared completeness evaluator. Build, review, management presentation, staff queue, current-release checks and seal status/action refuse absent, failed or ambiguous mandatory results. Scope is exact and review diagnostics follow authorization. Draft rendering and stored historical evidence remain available. Focused tests include all mandatory omissions/failures and a complete-format package with no failed row but a missing result; CI includes these tests.

CP-17 remains partial: a versioned final profile, actual cash/equity source resolution, reviewed disclosures, exact formatted composition decisions and the full browser journey remain pending. No professional or external gate was approved and no persistent database or deployment changed. Exact candidate fingerprints, checks and omissions are in `status.json`.

## Supplementary schedule provenance in pinned reporting — 2026-10-14

The presentation source manifest now binds the persisted current/comparative cash-flow, FX and equity rows, signed amounts, scope/currency/date/classification fields, cash populations and opening/closing evidence. Pin creation, read and freshness checks use the same versioned computation; the legacy contract field name remains unchanged. Old pins need explicit repinning to satisfy the strengthened freshness check; stored historical artifacts remain available.

This is identity protection, not accepted-source resolution, allocation validation or independent professional review. The source-backed schedule editor and full reporting journey remain incomplete; professional gates remain closed. No migration, deployment or UI changes were made. The subsequent dependency correction pins the already-used PKCS library to its patched version; NuGet audit enforcement is retained. Build/check evidence is tracked separately. Focused evidence and omissions live in `status.json`. Uncommitted audit-procedure concurrency work remains separate.

## Trial balance-to-report intake and signed equity — 2026-10-14

The attached AS-FR-CLOSE-001 story is mapped in [the delta review](auditsphere-execution-tracker-trial-balance-report-current.md), using existing task and methodology authorities. Current/comparative equity checks now preserve signs rather than accepting opposite amounts by magnitude. Focused presentation and retained AUD-04 regression checks passed; exact evidence is `targetedVerification.trialBalanceReportSignedEquity` in [status.json](status.json).

This is not full reporting completion. T003's qualified methodology/independent-review evidence is absent; draft/source-lineage warnings and existing review order remain. Source bindings, indirect cash flow, reviewed notes, exact final composition/decisions and full browser/live acceptance remain open. Pending procedure submission-lock changes from the interrupted workflow task are retained separately and are not part of this reporting delta.

## Task/professional boundary — 2026-10-13

Completing every task in a published pinned plan still refuses activation without Partner acceptance and creates no professional approval, opinion, release or freeze evidence. Template/plan checks passed; see `targetedVerification.workflowTemplateProfessionalBoundary` in [status.json](status.json). TASK-01 remains partial. Operational `RequiredTask` is not a professional gate.

## Release bundle integrity — 2026-10-13

Assembly rechecks signing policy and matching certificate on new creation/replay, including later certificate-required policy. All stored artifact hashes, including the representation template, are checked. Web forwards host policy; downloads remain scope checked. Focused Domain, Angular/reference journeys and built-in browser checks passed: ZIP/certificate integrity, lineage, stale sources and bounded correspondence with injected appends. Evidence: `targetedVerification.releaseBundleSigningReplay`, `releaseBundleCorrespondence` and `releaseBundleRaceAndAngular` in [status.json](status.json). Further adverse cases, full release and operator rotation review remain open. No global policy or persistent database changed.

---

## AS-AWP-EXEC Atomic Tasks 05–09 — procedure execution, evidence and truthful progress — 2026-10-17

Procedure submission and review now use an exact preview, fresh assent and an append-only command receipt; direct result and review writes fail closed. Submitted work retains a server-resolved evidence snapshot (stored link, physical file or manual reference) and a digest, distinct from later links. The native fieldwork page opens any authorized procedure for work, evidence selection and independent review, with lost-response recovery by receipt. Programme, area and fieldwork projections share the current-evidence evaluator; submitted or stale work is not counted as reviewed. Every one of the 165 source IDs retains the common workspace and a bounded existing-tool or manual-work support map.

Focused verification passed for Domain 9/9, support-map 1/1 and Angular 15/15. On `65a3f999`, fast CI passed; full CI passed Angular/contracts and is running unfiltered .NET suites. New API and synthetic browser journey evidence is pending those results. Task 10 remains blocked on D1; D2/D3 remain external. See [`status.json`](status.json#targetedVerification.atomicAuditWorkingProcessTasks05to09).

## Blazor rollback host retirement — merged — 2026-10-17

The owner authorized physical retirement of the Blazor rollback/reference host despite the canonical removal gate remaining `NOT_READY`. Branch `chore/retire-blazor-host` removes the legacy Web project, its solution and release-workflow entries, and the rollback-host-only E2E journey. The API-only owned host no longer exposes a rollback switch. Historical source reviews and the retirement inventory link to the pinned pre-removal commit; the discovery inventory is retained rather than emptied, and the UI inventory gate recognizes the retired source. The owner merged the branch to `master` as `e7ebe27c`; the retired host is no longer in the current tree.

Focused verification on the branch: Release solution build passed with 0 warnings/errors; focused API tests passed 15/15 and Domain architecture guards 3/3; documentation health, filenames, narrative metrics, UI inventory, retirement inventory, OpenAPI drift and `git diff --check` passed. No full suite, hosted CI, deployment, production rollback or external acceptance was run. Evidence is in [`status.json`](status.json#targetedVerification.atomicBlazorRetirement).

## R2R-07 and CP-11 — 2026-10-17

## AS-AWP-EXEC Atomic Task 02 — exact engagement programme adoption — 2026-10-17

An engagement can now inspect only published programme versions within its authorized scope, including exact source/content identities and explicit eligibility blockers. Adoption is separate from firm methodology publication and must submit the selected version's source hash and digest. The service verifies the immutable catalogue against the current source, creates all 165 controlled baseline procedures atomically, reports baseline and ad-hoc counts separately, safely replays the same adoption, and refuses a stale or different-version adoption without overwriting in-progress work. The Angular fieldwork screen now selects an explicit eligible version; the retained Web caller also uses the exact options projection.

Focused verification: Domain adoption regression 1/1; API adoption/options regression 1/1; Angular fieldwork spec 12/12; E2E test project compilation passed (0 warnings/errors); API and Web Release builds passed (0 warnings/errors); OpenAPI drift and `git diff --check` passed. No schema change was needed. The browser journey was not run because no authenticated local synthetic fixture was available. CI for `8b0c3303` found stale generated Angular source inventory; that inventory has been regenerated locally and is queued for the follow-up documentation commit. CI remains pending for the corrected tree. This does not constitute full-suite or professional methodology acceptance. Evidence is in [`status.json`](status.json#targetedVerification.atomicAuditWorkingProcessTask02).


## Accessibility and CI entries moved from the current slice on 2026-10-20

The following sections were moved verbatim to keep the active handoff concise. Their findings remain historical; current accessibility and CI status is recorded in the active current-slice section and `status.json`.

## Versioned reminders and accessibility CI follow-up (2026-10-20)

Reminder drafts are revision/digest-fenced; publishing requires independent review; API verifies firm-wide denial (403). Fixtures clone the fingerprinted migrated template; partial migrations remain fresh. Focused API cohorts passed 9/9 and 6/6. Fast CI [39402922430](https://github.com/nirzaf/AuditSphere/actions/runs/39402922430) passed on `e1827855`. Full release [39395144233](https://github.com/nirzaf/AuditSphere/actions/runs/39395144233) on `6bce7e77`: Angular (1,487), Domain (1,457), build/contracts and API passed; E2E is running. Superseded [39380954200](https://github.com/nirzaf/AuditSphere/actions/runs/39380954200) hit PostgreSQL `57P03` and runner exit 143.

Playwright [39359432361](https://github.com/nirzaf/AuditSphere/actions/runs/39359432361) passed 325/325, including six accessibility checks; its API job failed, so it is E2E-only evidence. UI/E2E sources are unchanged through `6bce7e77`. Browser smoke on `e1827855`: My Work rendered without console errors; at 320px document width was 305px; skip link reached main; navigation trapped/wrapped Tab and Shift+Tab, and Escape restored trigger focus. Port 5029 served a stale bundle with missing chunks; its blank route is not current-source evidence. Human screen-reader review and full supported-state coverage remain open.

## Mobile navigation accessibility follow-up (2026-10-19)

The compact navigation dialog now explicitly exposes modal semantics, and its close button uses a foreground that meets contrast on the dark surface. The focused navigation spec passes 11/11. In the built-in browser, 320px reflow, dialog naming/background hiding, focus entry and wrapping, Escape closure/focus restoration, and contrast (6.70:1 active link, 12.02:1 other links, 17.06:1 close button) were observed; 640px reflow has no horizontal overflow. Evidence is in `targetedVerification.angularQuality.task07.browserFollowUp20261019` in [status.json](status.json).

At 200% native Chrome zoom, the CSS viewport/document widths were 919/912px; at 400%, 459/456px. The skip link focused the visible main landmark at 400%, and both zoom levels had no horizontal overflow. The responsive navigation remained operable. Business screens are still not accepted because of the API/UI mismatch. Qualified screen-reader review, complete workflow-state coverage, locale/device acceptance and a conclusive full release run remain open.

## Accessibility records and CI failure repairs (2026-10-19)

This follow-up extends the earlier empty-state checks with current-source, record-backed synthetic journeys. Planning preview now receives meaningful focus without stealing it from another owner, cancel returns focus to the native review button, and one persistent status region announces outcomes. Consolidation tables have accessible names and native disclosures have one control. Sampling history review visibly requires opening the exact set. Finding facts stack at narrow widths rather than overflowing the document.

Further rendered checks found focus loss in independent budget approval and staffing previews. They now focus named review regions; cancel returns to fresh initial assent, or the exact authorized team row for revocation while leaving its canceled intent cleared. Existing journeys retain their receipt/authorization assertions and now exercise keyboard review, cancel, fresh assent, preview refusal and narrow populated layouts. The procedure journey also verifies keyboard submission/review and cancel-by-edit without restoring assent. Focused reruns pass after synchronizing keyboard traversal with enabled controls. The complete Domain suite has passed in GitHub Actions on the preceding repair source; API/E2E and final-source acceptance remain separately pending in the ledger.

A delayed real preview exposed another focus boundary: clicking the nonfocusable header while waiting left native focus on the document body, so the previous fallback stole focus when the response arrived. Preview focus now also requires an unchanged pointer/keyboard interaction generation. The regression first failed on the old helper and passes after the correction, alongside the existing preparation, approval and staffing journeys. Ordinary keyboard preview/cancel and narrow reflow were rechecked in the built-in browser; independent source review found no blocking issue. Fast CI passed on the correction source.

A real intercompany form journey exposed options refresh recreating controls and losing focus. The form now retains owner-bound display options while assent and submission still require the exact current verified resource. The populated member selectors also needed a scope-local flex sizing correction for narrow screens. Focused checks and built-in native keyboard review now preserve the editable control, exact signed retained amounts, maker/checker restriction and bounded table scrolling. The fixture creates only an unapproved outside-perimeter review record; approved-component consolidation and the complete state matrix remain separate acceptance work.

The live E2E run subsequently exposed a second search test retaining a combobox locator after autocomplete changed the native input's role. Its initial combobox assertion remains; later Escape and focus checks use the same labeled input. The focused shortcut journey passes without removing its form-field slash checks. The full run remains active with a reported failure, so its passing Domain/build jobs are not a full release pass. Earlier detailed accessibility cohorts are archived intact with an active pointer and their unresolved native-keyboard/human-review limits retained.

Built-in-browser checks used the current UI and an owned current-source API with disposable PostgreSQL fixtures. Budget preview/cancel, invalid input, fresh assent, populated consolidation journal lines, keyboard disclosures and bounded horizontal scrolling were inspected. No professional approval or financial command was confirmed interactively. Focused owned-fixture journeys also cover preparation/recovery, exact sampling review/execution and authorization denial. The earlier unknown-source API observations remain separately attributed below.

CI exposed a stale commercial fixture: prospect proposal delivery must occur before client conversion invalidates the prospect recipient authority. The test now follows that ordering while retaining its mail/voucher assertions. Browser regressions required awaiting deferred planning data, retaining a labeled search input after its autocomplete role changes, and testing skip-link reachability from intentional route focus through actual keyboard navigation. Exact receipt recovery passed focused reruns without reproducing its CI failure; added HTTP diagnostics preserve that uncertainty for the next run.

The remaining native sampling result-selection check is now evidenced through real keyboard typeahead and Tab commitment. The exact existing journey retains its source identities, stored exception amount and independent-review assertions. Its built-in browser inspection changed an unsaved synthetic draft only; native arrow assumptions and an immediate assertion before the change committed were test-setup failures, retained separately from the passing final check. Human assistive-technology and complete supported-state acceptance remain open.

The subsequent API job reported two stale array assertions against the paged consolidation contract, then received a runner shutdown signal without a complete suite outcome. The existing journeys now read page items and retain the original authority, composition refusal and review assertions, with exact journal identity and count checks added. Both repaired methods pass focused verification. A fresh full immutable-source CI run includes these repairs and the accessibility fixes; the preceding E2E run remains active for further failure evidence. Superseded CI records are archived intact with an active pointer.

The failed older-source full run was superseded after preserving its reported failures. A fresh immutable-source full regression runs in GitHub Actions with per-test progress and hang diagnostics; every suite remains unfiltered. Full suites were not run locally. See `targetedVerification.angularQuality` in [status.json](status.json) for precise commits, results, artifacts and CI status. Human screen-reader review and the complete supported-state matrix remain open; focused synthetic passes do not establish whole-product accessibility or release acceptance.

Earlier fieldwork command-safety and shell zoom/reflow observations are preserved intact in the “Angular quality follow-up — fieldwork safety and rendered acceptance” section of [the historical handoff](auditsphere-execution-handoff-history-2026-10-historical.md). Current source identities and unresolved human, native-keyboard and full-regression acceptance remain explicit in the active ledger.

The earlier Relationship Manager search-matrix regression and its historical CI evidence are retained verbatim in [the historical handoff](auditsphere-execution-handoff-history-2026-10-historical.md#relationship-manager-search-matrix-regression-2026-10-18).

## GL upload checkpoint accessibility and CI follow-up — 2026-10-19

The GL upload now distinguishes secure-checkpoint initialization failure from the temporary preparing state. On failure it keeps file selection disabled and announces that upload is unavailable, with a reload recovery instruction. Focused Angular verification passed for `gl-upload.spec.ts` (7/7), and changed-file ESLint plus `git diff --check` passed. Commit `651a12e9eed73e73edc08fa2eff665c2cc0e4376` is pushed to `master`.

The built-in browser could not connect to the local UI at `127.0.0.1:4300`, so no authenticated synthetic screen was available for another live browser pass. The full release run on `651a12e9` exposed one stale Domain assertion that expected a generic work-queue link; the product emits and the Angular route consumes the exact engagement and task IDs. The assertion now checks that scoped URL, and its focused Domain test passed 1/1 in a clean checkout. The full unfiltered Domain suite then passed 1,445/1,445. Corrected source is `5f20861c2a29c11c1114cb6f66f8b43a29c6226d`. Fast CI run `39291585076` passed Angular, docs health and Release build. Exact-SHA release run `39291772192` passed ref resolution, Angular/full specs and build/contracts/publication; API and Playwright E2E suites remain in progress. E2E includes the 20-route semantic, control-name, form/table, contrast, Escape and locale sweep. Human screen-reader review by a qualified reviewer and broader rendered-state acceptance remain open; do not mark accessibility complete.

2026-10-20: current E2E fixes on `442e1b5a` pass 17/17; CI and human AT status are in [status.json](status.json).

## Reminder schedule bound — verification checkpoint 2026-10-20

Uncommitted implementation adds an explicit reviewed 1–100 schedule-window limit for repeating reminders; legacy unbounded revisions require replacement, canonical occurrence keys prevent alias duplicates, and queued valid windows retain retry recovery. Release build passed (0 warnings/errors); changed Domain cases 3/3, Angular specs 4/4, lint, EF drift and OpenAPI passed. Angular production build passed with existing completion-template and commercial stylesheet warnings. Built-in browser verified missing-limit refusal, saved/read-back repeat-7/max-3 draft, named limit/help text, one-time limit removal and zero console errors in an owned synthetic fixture. This exposed UTC-format receipt mismatch; comparison now accepts equivalent instants while retaining sub-millisecond distinctions, with the existing save spec covering server normalization. Affected specs (4/4), build and component lint passed again; the deep accessibility fixture passed (1/1). Mobile 390×844 inspection passed: bound/help usable, no horizontal page overflow or console errors; owned accessibility E2E passed 1/1. Current-source full CI remains pending; no live enablement or deployment occurred. Full release run 39468155496 for earlier a97ce47d remains running: Domain, Angular and build/contracts passed; API/E2E still running at observation. Human screen-reader acceptance remains open; automated review cannot supply that approval.

## Preserved reconciliation delivery note — 2026-10-20

## Documentation reconciliation — initial delivery (historical)

The initial documentation slice was delivered through PR #46, and the companion API/Angular integration through PR #47; both are merged to `master`. This paragraph records delivery history only. Current dispositions, evidence and open gates are in the [reconciliation report](auditsphere-execution-report-documentation-reconciliation-current.md) and [`status.json`](status.json). D1-DRAFT-01 remains an unapproved review packet; qualified T003/G16/EOS and external acceptance requirements remain open.


## Reminder schedule bound — 2026-10-20

Implementation is pushed at `a7df1799`; focused and mobile evidence is retained in `workflowReminderScheduleBound20261020` in [status.json](status.json) and the [historical handoff](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md). Current-source full release and human AT acceptance remain open; no deployment occurred.

## Template evidence prerequisites — 2026-10-20

Reusable templates now declare materiality, exact PBC and exact procedure prerequisites. Plans bind authorized engagement sources, retain immutable origin and recover receipts without duplicate tasks. Task completion still cannot approve professional evidence. Focused PostgreSQL, Angular and browser evidence, repaired HTTP/activity issues and release limits are tracked under `templateEvidencePrerequisites20261020` in [status.json](status.json). The [operator guide](../operations/auditsphere-operations-guide-workflow-template-evidence-current.md) describes configuration and recovery. Broad supported-state, human AT and full current-source release acceptance remain open.

## Brief quotation timeline — 2026-10-20

New brief quotations require a timeline; drafts and historical recovery remain supported. Focused and browser checks passed; current release is pending. Evidence: `quotationTimeline20261020` in [status.json](status.json).

## Practice time phase

Optional phase capture and retained display pass focused/API/browser checks. Full acceptance remains open; see `practiceTimePhase20261020` in `status.json`.

## Firm reports and finding recovery

Report paging/export now binds one source identity; finding recovery waits for synchronized context. Focused and browser checks passed. Full current-source CI remains pending; see `firmReportIdentity20261020` and `releaseRun39475043022` in `status.json`.


## Recorded communication history

The management-only outbound history now counts its complete authorized population while bounding displayed rows. Counts and rows share a database snapshot, followed by a fresh scope/grant check. Payment vouchers follow their retained document and agreement; provider acceptance is never shown as delivery time. The table has explicit headers, descriptive links and a named keyboard-scroll region. Evidence and fixture limitations are under `communicationHistory20261021` in [status.json](status.json). Client-public chronology, full actor/time proof, human AT and full release acceptance remain open.

## Exact-period accounting evidence

The accounting period page exposes separately paginated retained books, sources, completeness, reconciliation proofs, specialist schedules, PBC date matches and tasks. Each evidence page uses client-wide authorization, a consistent database snapshot and a fresh authorization fence before returning. Exact revisions remain strings. Receipt and proof history is not a current-validity or readiness decision. The category selector is verified against its displayed table; scope changes clear old results. Details and limits are recorded under `accountingPeriodEvidence20261021` in [status.json](status.json).

ASPM-ACC-05 remains partial: typed bank/AR/AP work packs and approved monthly handover acceptance are not supplied by this view. Human screen-reader acceptance and full current-source CI remain separate gates. No deployment or provider action occurred.

## Client final-bundle history

The portal shows retained assembly time in UTC, separate from client-reported receipt. Each download has an exact accessible bundle identity. The collection reader now uses the existing archive bound with an overflow sentinel instead of silently dropping bundles after the tenth per engagement. Original bytes, hashes, acknowledgement and client scope remain intact. Focused evidence is under `portalBundleHistory20261021` in [status.json](status.json). Signed-representation history, complete client-public chronology, contextual labels, human AT and full current-source release acceptance remain open. No live delivery or professional acceptance is claimed.

## Work-table keyboard scrolling and reduced-motion follow-up (2026-10-20)

My Work’s two horizontally scrollable tables now explain their keyboard controls through `aria-describedby`. When the named region itself is focused, Left/Right scroll by a visible step and Home/End move to the bounds; key input from links, buttons and fields is left to those controls. The reduced-motion preference reduces the observed transition from 150ms to 0.01ms.

At a 390×844 CSS viewport, the page had no horizontal overflow. Built-in-browser verification moved the operational table from `scrollLeft=0` to 272 with ArrowRight, to its 580px end with End, and back to zero with Home. The focused `work-queue.spec.ts` passed 32/32; the Angular development build succeeded with four existing NG8107 optional-chain warnings in `src/app/features/audit/completion.ts:128-133`. No full suites were run locally, and no business records changed.

Fast CI run [39420353260](https://github.com/nirzaf/AuditSphere/actions/runs/39420353260) passed docs health, Angular build/specs, .NET Release build, fast architecture/rounding guards, EF model drift, publication and OpenAPI drift on source `4f6b964e`. Acceptance-image run [39420353130](https://github.com/nirzaf/AuditSphere/actions/runs/39420353130) passed API, worker and migration image builds; it did not deploy. Docs Health run [39421407299](https://github.com/nirzaf/AuditSphere/actions/runs/39421407299) caught the 100KB `status.json` budget after this evidence was added. The previous detailed sweep is now in `status-archive-2026-10.json`; after the concise current-source update below, the ledger is under the 100KB limit. Fast CI run [39421714616](https://github.com/nirzaf/AuditSphere/actions/runs/39421714616) passed Angular, Docs Health and .NET Release build on exact source `6546ffb7`. Full release run [39410131489](https://github.com/nirzaf/AuditSphere/actions/runs/39410131489) completed with failure on earlier source `9941a935`: Angular, Domain, API and build/contracts/publication passed; E2E passed 322/325 and failed three cases. Those were `AngularWorkpaperCommandJourneyTests.LostResponseReloadRetainsExactIdentityAndRecoveryNeverBlindlyResends` (SAVE_DRAFT/committed/superseded), `AngularFirmScopeRevocationParityJourneyTests.ErroneousClientAdministratorGrantIsDeniedAndRevocationClearsNativeWorkspaces`, and `AngularAcceptanceChecklistWorkspaceJourneyTests.StaffChecklistShowsScopedWorkspaceProtectsDraftOnRefreshAndClearsAfterRevocation`.

The subsequent current-source browser follow-up used the Angular dev server on port 4304 with synthetic data from the local API on port 5029. At 919×436 CSS (200%-equivalent), 459×218 CSS (400%-equivalent), and 320×844 CSS, document/body widths were 904/904, 444/444, and 305/305 pixels; no page-level horizontal overflow occurred. These were explicit CSS viewport overrides, not native browser zoom. Keyboard Tab reached the operational table with visible focus; ArrowRight, End and Home moved its horizontal offset 0→325→514→0 at 459px, and End/Home moved 0→650→0 at 320px. Both named table regions expose instructions, no unnamed controls or browser console warnings/errors were observed, and no business data was changed. A current-source filtered rerun of the three implicated E2E classes passed 9/9, including all seven workpaper theory cases; the CI failures did not reproduce. A separate full-run attempt failed before tests because GitHub could not fetch obsolete source `9941a935`; no code tests ran in that attempt. Full API/E2E verification on current source remains open. The requested local Release solution build passed with 0 warnings and 0 errors; no full test suites were run locally.

Qualified human screen-reader review, full supported-state and client-portal acceptance with a client identity, current-source API-backed resource-planning acceptance, and full API/E2E regression on current code remain open. The detailed evidence is in [`targetedVerification.accessibilityRenderedSweep20261020`](status.json).


## Signed-scan identity and portal CI fixture correction

Retained scan rows now show the management-provided signatory, source-letter version, UTC upload time, hash and distinct accessible download identity. Source currentness and a separately recorded verification decision are displayed independently; upload or acknowledgement does not establish authenticity. The real client journey caught narrow-screen overflow from long labels; wrapping columns now preserve reflow. The parent portal fixture now includes the required server context while retaining its draft, file, polling and in-flight command checks. Focused results and limits are under `portalScanPresentation20261021` in [status.json](status.json). Full release, complete public chronology and qualified human AT acceptance remain open.

## Client document context and retained history

Shared documents and final bundles now show the exact authorized client legal name, recorded service and reporting dates. Empty historical fields are explicitly not recorded. One bounded read snapshot covers context and evidence; fresh session, grant-set and context checks run before returning it. A changed grant refuses the payload; changed context requires a refresh. The client signed-scan archive uses the existing aggregate bound and overflow refusal, while the staff latest-history limit stays unchanged. Bytes, hashes, review decisions and receipt commands are preserved.

Focused checks and owned mobile-browser evidence are under `portalDocumentContext20261021` in [status.json](status.json). Earlier bundle evidence is preserved in the [historical archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md). Complete client-public chronology and actor/time proof, qualified human screen-reader review, full current-source CI and professional/live-provider acceptance remain open. Owner authorization does not constitute human assistive-technology review; no such sign-off was recorded.


## Workflow evidence source discovery and keyboard continuity

Operational plan previews now discover exact-engagement PBC and procedure references through a lazy, paged search. Current selections resolve independently of the search/page; manual identifiers remain available. Recorded status is informational, including draft requests and planned procedures. Snapshot reads end with fresh authorization; session/context changes refuse stale results. Selecting a source clears the old preview and assent and restores keyboard focus to the persistent preview action. Preview and task completion still enforce source scope and professional evidence independently.

The owned browser exercised both bindings and mobile reflow without creating tasks or approving professional evidence. The focused automated journey separately created the exact operational rules and confirmed completion remained blocked. Source identity, focused results, repaired checks and CI limits are under `workflowEvidenceSourcePicker20261021` in [status.json](status.json). WF-03, whole-state accessibility, qualified human AT, current-source full release and professional/live-provider acceptance remain open. No deployment or Microsoft configuration changed.


## PBC request snapshots and terminal delegation management

Request detail and history now read one consistent snapshot, then recheck the current client session, participation, request metadata and capability inputs before returning content. Lost scope is refused; changed requests or capabilities require refresh without a partial payload. A primary owner can still inspect and revoke existing delegations after request acceptance/closure; creation remains forbidden. The separate management flag preserves that authorized recovery path without exposing new candidates.

Conversation paging keeps its existing chronological order and now uses the matching forward label. Conversation and upload-intent times show UTC with retained semantic timestamps; intent creation is not provider completion. Focused cases first reproduced stale success responses, then verified the fences, closed-request revocation and normal client journey. Results and exact source are under `pbcRequestReadSnapshot20261021` in [status.json](status.json). Full public chronology, whole-state/qualified human AT, current-source full release and live-provider/professional acceptance remain open.

## Retained PBC reviewer acceptance in the client portal

Client request detail now distinguishes a retained exact-upload acceptance receipt from the current request status. The receipt’s file identity, revisions, hash, byte count and UTC decision time survive closure; reviewer IDs and provider internals remain private. Legacy terminal statuses without that receipt do not create an acceptance event or time. The existing snapshot, participation and current-session fences still govern the read. Angular keeps retained integer strings exact and refuses malformed supplied receipt content.

Focused database, API, Angular, real local-command browser and built-in mobile observations are under `portalPbcAcceptance20261021` in [status.json](status.json). Local provider simulation is explicitly not live tenant or qualified professional acceptance. CLIENT-02/04, complete client-public chronology, whole-state/human AT and current-source full release remain open. Earlier signed-scan and document-context handoffs are preserved in the [historical archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md).

## Native template authoring and pinned queue labels

A focused synthetic browser journey now authors a Required and Optional task template through Angular, uses a separate Partner session for independent review and publication, previews and creates an engagement plan, and follows its exact pinned source identities through Team list, board and calendar. Manual tasks remain undesignated; creating the plan completes no task and grants no professional approval. Independent review strengthened the exact manual identity and queue membership assertions.

Built-in narrow-screen inspection confirms retained labels and reflow. Source, focused results, artifacts and exact CI scope are under `workflowTemplateLineage20261021` in [status.json](status.json). The reminder criterion now records the existing reviewed internal-rule catalogue rather than calling it absent. Required-task graph semantics, broader workflow acceptance, human screen-reader review and professional/live gates remain open. Earlier request-snapshot notes are preserved in the [historical archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md).

## Workflow evidence downgrade-test recovery

The historical full release failed in one API migration probe after later compatible down-migrations removed a nullable lineage column and an older retained-evidence guard correctly refused further rollback. The test now reads the retained identity in that intermediate schema, restores the latest migrations, then checks the original whole-table single-record invariant and retained fields. Production migrations, refusal guards and authorization assertions are unchanged. Exact source, focused results and historical release outcome are in `workflowEvidenceDowngradeRecovery20261021` in [status.json](status.json); this is not a current full regression pass.

A further defensive audit found that several client command paths reauthorize the pre-lock request owner rather than the locked row. No supported owner-reassignment command currently exists, so this is latent hardening work, not a demonstrated reachable portal exploit. Any correction must retain scope checks and lock order and prove current participation without adding a reassignment workflow.

## Current PBC authority and upload lock order

Core PBC request commands now refresh the locked row before authorizing the current participant and refusing scope drift. Upload and trusted-completion paths take a consistent scope fence before request/intent locks. A coordinated PostgreSQL regression reproduced the old completion/replay deadlock before the fix. Exact receipt replay and the staff-only trusted completion boundary are preserved.

The locked start/resume writability checks retain a saved refusal audit without creating or modifying uploads. This does not define a comprehensive client-denial audit policy or introduce request-owner reassignment. Direct owner-change test interleavings are defensive evidence, not a claim of a reachable reassignment exploit.

Source, red/green evidence, public API/browser checks, built-in inspection and CI limits are under `pbcCurrentAuthority20261021` in [status.json](status.json). Broader portal, current-source full release, professional/human and live-provider acceptance remain open. The earlier migration-probe and pre-fix latent-owner notes are preserved in the [historical archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md).

## Unbilled-time report session boundary

The manually loaded firm-finance report now clears retained amounts on session invalidation or identity change, invalidates pending work on destruction, and suppresses stale completions/errors. It does not automatically query the new identity or change server authorization, valuation, WIP policy, contracts or records. Focused red/green evidence is recorded under `unbilledTimeSession20261021` in [status.json](status.json). Human accessibility review and current-source full release remain open.

The previous PBC authority and lock-order slice remains locally verified; complete evidence and prior handoff are preserved in the [historical archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md), with its status pointer retained.

## Native invoice sources through monthly client books

The existing native invoice/credit/allocation browser journey now reconciles exact posted source journals to two months of GL and Trial Balance activity. Partial allocation remains distinct from credit posting; client sources do not mutate firm books or imply cash settlement. Named keyboard-scroll regions and table headers cover the retained preparation/posting and report tables; allocation controls reflow within the workspace.

The populated browser check exposed and repaired page overflow while preserving intentional table scrolling. Exact arithmetic, focused results, intermediate failures, built-in inspection and CI limits are recorded under `nativeInvoiceSourceReport20261021` in [status.json](status.json). Synthetic service setup does not supply a real client mandate or professional acceptance. Settlements/reversals, full current-source release, human AT and live gates remain open. Prior session-bound finance/PBC notes are preserved in the [historical archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md).

## Source-backed procedure submission and rework

The fieldwork review screen now summarizes only the retained submission's evidence identities and observations, distinctly labeling physical locations at submission and unverified legacy references. It never reconstructs evidence from current links or asserts source freshness/professional sufficiency. Unsupported formats retain the raw-snapshot inspection path; both views reflow.

The existing owned browser journey now connects synthetic received PBC bytes to exact Angular evidence selection, lost-response recovery, independent return and corrected resubmission. Frozen identities, hash and receipt digest remain linked while the later rework-queue checks are preserved. Results, narrow-screen inspection and limits are under `procedureSourceEvidence20261021` in [status.json](status.json). No qualified human, methodology or live-provider approval is supplied; broader state/freshness and current-source full release acceptance remain open. Earlier native-book/session evidence is preserved in the [historical archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md).

## Retained shared-document milestone times

Client document cards now show the retained sharing time and document acknowledgement as separate semantic UTC times. Legacy missing share metadata is explicitly unrecorded. Acknowledgement belongs to the shared review record and is not attributed to the current account; internal actor identities stay private. Existing snapshot, final authorization/context fences, review controls and signature/provider boundaries are unchanged.

Exact source, focused API/Angular/real-command browser results, fixture repairs, built-in narrow-screen observations and artifacts are under `portalDocumentMilestones20261021` in [status.json](status.json). This advances client-visible history but does not complete a unified chronology, qualified human accessibility review, current-source full release or professional/live acceptance. Earlier PBC acceptance notes remain in the [historical archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md).

## Generated task assignment integration

The existing template journey now assigns one generated task through the engagement UI, verifies its exact owner in the refreshed workspace, and checks that task in the staff owner's My Tasks. Built-in inspection distinguished an exact label-text locator failure from the correct native accessible name; the test uses the combobox role/name. No product behavior or permission change was needed. Evidence and limits are under `workflowTemplateAssignment20261021` in [status.json](status.json). ASPM-UAT-05 remains partial for review, return, rework and wider denials.

## Annual engagement source-context safety

Annual route changes now invalidate previews, assent and late callbacks while retaining unsaved fields and durable receipt recovery. A retained annual draft cannot silently become manual creation; returning to its source requires fresh review. The dynamically populated template selector now displays the retained revision correctly.

The existing annual journey covers foreign-source denial, stale-template refusal, current contact/template refresh, draft save/restore/return and separate current-year task planning. Creation remains blocked with no inherited approvals or access grants. Focused results, intermediate failures and limitations are under `annualSourceSafety20261021` in [status.json](status.json). Full CI found two unrelated E2E failures now under investigation; human screen-reader, professional and live-provider acceptance remain open. Earlier procedure evidence is retained in the [historical archive](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md).

## Unbilled standard-value criterion audit

FIN-05 is complete for its specified non-WIP labeling behavior: source and focused query/rendered-notice checks prove the allowed standard-value fallback. Recognized WIP policy and all wider accounting/release acceptance remain separate. Exact evidence is in `nonWipLabelCriterion20261021` in [status.json](status.json).

## Independent time approval paging

The approval workspace no longer silently stops at its first retained batch. Bounded work-date/ID continuation keeps current reviewer scope, self exclusion and task/entry context filtering before the page limit. The UI labels live pages as shown, preserves recording drafts and open-return intent, restores keyboard focus and refreshes from the start after review actions. Missing legacy metadata is visibly incomplete.

Focused API, Angular, built-in and scoped browser evidence is under `timeApprovalPaging20261021` in [status.json](status.json). The browser proves the default page boundary and no mutation during paging; it does not supply human/professional/live acceptance. Own-entry/task collection bounds and broader state/role/full-release gates remain separate.

## Recorded-time history continuation

Own recorded time now continues through bounded work-date/creation/ID pages instead of silently stopping at a retained batch. The backend filters current owner, grants and matching task context before limiting; cursors retain stored timestamp precision. Own history and approval positions remain independent. The UI preserves recording/return drafts, guards focus, exposes failed-page recovery and permits read-only unknown-save reconciliation without discarding an open return reason.

Focused API/Angular and the existing real browser rate/task/time journey pass; Release solution build and OpenAPI drift pass. Exact evidence, the repaired duplicate-ID test fixture and verification limits are under `ownTimeHistoryPaging20261021` in [status.json](status.json). This is not human screen-reader approval, full current-source release acceptance or live-provider acceptance. Broader collection bounds and role/state acceptance remain open. Previous approval-paging evidence is preserved in the archive.

## Statutory schedule preview/save consistency

Preview and save now derive defaults from the same existing calculator. Tight valid schedules no longer fail because of different preview-only dates. Out-of-range date arithmetic returns a failure instead of an unhandled exception; malformed nonblank optional dates return validation errors rather than silently using defaults. Warning overrides, configure-role authorization, locking and immutable revisions are preserved.

Focused Domain and real HTTP checks pass, including exact independently derived dates and no-write refusals. Evidence is under `milestonePreviewValidation20261021` in [status.json](status.json). No new statutory calendar rule or professional deadline approval is supplied; dependent-task amendment effects, broader scheduling acceptance and current-source full release remain open. Recorded-time paging evidence remains available through its status/archive pointer.

## Coherent proposal acceptance projection

The proposal workspace now reads proposal status/history, response evidence and dispatch preview within one owned database snapshot, then disposes it before checking current authorization again. A concurrent acceptance can no longer combine the old proposal state with a new response/history. Caller-owned transactions are refused without being consumed; the response contract and commercial approval rules stay intact.

The controlled acceptance test reproduced the mixed-state defect on the original query, then passed with the fix. Focused exact-offer, firm/role/fee, current-revocation and caller-transaction checks pass. Evidence is under `proposalSnapshotConsistency20261021` in [status.json](status.json). Snapshot consistency does not promise newest business facts or consistency across larger composite workspaces. Broader commercial/professional/live/current-source release acceptance remains open. Scheduling evidence is preserved through its archive pointer.

## Composed onboarding readiness snapshot

Onboarding now reads its approval, risk, letter, advance, hold and activation facts in one owned snapshot. Internal authorized fee/proposal readers compose that snapshot without nested transactions. Public reads recheck current authorization after disposal and refuse lost authority behind true action flags, including Partner or Finance loss while commercial access remains. As-of time is captured at snapshot start; protected commands retain their independent checks.

The controlled baseline failed its report-time assertion; all focused fixed-source checks, including existing paid-advance readiness, pass. Evidence and precise limits are under `onboardingSnapshotComposition20261021` in [status.json](status.json). No professional/live/full-current-source acceptance is inferred, and no UI/provider changes were made. Previous proposal evidence remains through its archive pointer.

## Correction queue to fieldwork handoff

Returned/resubmitted task rows now offer Open fieldwork in list, board and calendar. The link uses the task's server-projected engagement; it grants no authority and does not infer a procedure from a source key. Fieldwork still rechecks current scope/gates and requires explicit procedure selection.

The existing source-backed procedure journey now routes its original preparer through the assigned correction queue before corrected resubmission. Exact task/owner/comment, keyboard routing and retained evidence lineage are checked. Built-in inspection confirmed the destination's existing blockers. Evidence/limits are under `procedureReworkHandoff20261021` in [status.json](status.json); broader integrated denials, human/professional/live acceptance and full current-source release remain open. Earlier assignment evidence remains in the historical archive.

## Portal finance scope before bounds

Exact active grants now filter agreements before the result limit, so newer sibling records cannot hide an authorized agreement. The extended existing regression fails on the old query and passes on the fix. Evidence and remaining window/snapshot limits: `portalFinanceScopeBeforeBound20261021` in [status.json](status.json).

## Attributable time edits and approved corrections

Draft and returned entries can now be revised from Angular after exact review, without submitting or approving them. Approved corrections supersede the original and create a separate draft. Immutable actor-owned receipts preserve before/after values, reasons and revisions; uncertain outcomes use authorized receipt lookup and explicit replay of the same request. Existing rate, mapping, invoice-reservation, scope and independent-review controls remain enforced. Legacy records are not assigned invented historical actors.

Focused Domain/API/Angular and real browser evidence passes, including a deliberately lost committed response. Built-in inspection confirmed retained history and keyboard focus with no captured browser warnings/errors. Exact checks and limitations are under `timeEntryChanges20261021` in [status.json](status.json). Migration was exercised only in disposable test databases; no deployment, full current-commit release or human screen-reader approval is claimed.

## Release E2E failure triage

The accounting revocation journey now handles the expected removal of refresh actions during session revocation, then still requires the denial screen and removal of protected content. Its focused browser regression passes. The finding lost-committed test now proves the forwarded server command succeeded before receipt recovery; its previously failing case passes. The old CI log cannot prove the exact race, and product conflict/authorization guards remain intact. The subsequent full release run failed three different browser observations: two account labels are row headers, and a paged evidence response body was read after navigation. Semantic row-header locators and the existing response-capture helper preserve the assertions; all three focused cases now pass. A fresh full run on the committed fixes is still required. Exact evidence and scope are under `releaseFailureTriage20261021` in [status.json](status.json). No full local suites or external changes were performed.

## Client-visible activity across retained sources

The portal now pages retained document, PBC, invoice and payment-receipt facts together. Current exact grants and request participation constrain every source before bounds; a scope-bound UTC cutoff controls continuation. The owned snapshot is followed by fresh authorization/context checks. The feed omits message bodies, amounts, staff IDs and provider metadata; receipt and sent timestamps never imply delivery. Source links preserve the portal route and keyboard destination.

Focused query/Angular and the real completion-to-client journey pass after repairing an EF projection-ordering failure. Built-in inspection observed nine retained events with UTC/source links and no captured warnings/errors. Evidence and limits are under `portalActivity20261021` in [status.json](status.json). This is live history, not a frozen archive; whole-source/state, human AT, professional/provider and full current-source release acceptance remain open. Prior time-revision and finance-bound evidence is preserved in the historical archive and status pointers.


## Fresh locked workflow amendments and quotation privacy

Evidence-rule and dependency amendments now refresh locked EF-tracked task/binding rows before validating identity, replay, revision and closed state. Both stale-context baselines attempted writes rejected by existing database guards; the fixed commands refuse cleanly without new rules, edges or activity. Four focused cases pass, including existing positive/replay paths. No UI, contract or migration change was needed.

The existing branded-quotation theory also passes both standing-authority variants with synthetic private cost values/IDs excluded from the document body and required commercial fields retained. This is body-content evidence, not a broader approved allowlist or legal/layout acceptance. Exact sources, artifacts and limitations: `workflowAmendmentFreshLocks20261021` and `quotationCostPrivacy20261021` in [status.json](status.json). No full local suite was run; current-source full release and professional/live/human acceptance remain open. Earlier client-activity evidence is preserved through its status pointer and historical handoff.

## Consolidation workspace read consistency

The multi-panel workspace now owns one read snapshot and refuses caller-owned transactions. After disposal it rechecks group/context and current source approval readiness; capabilities can only be retained or demoted. The real reviewed-package regression proves source access withdrawal while group access remains, and concurrent approval no longer mixes component/readiness states. Three unique focused cases pass across the initial run and corrected-fixture rerun. Evidence/limits: `consolidationWorkspaceSnapshot20261021` in [status.json](status.json). Existing command guards, source books and professional approvals are unchanged.

## Client finance agreement history and account isolation

Client finance now pages exact authorized agreements instead of silently stopping at the newest collection bound. Stable retained creation-time/ID cursors, explicit older/newer/refresh controls and a consistent page snapshot preserve financial facts. Each response rechecks current identity and exact grants after snapshot disposal. Linked invoices must belong to the agreement client's billing account; receipt and credit totals exclude foreign accounts.

The focused PostgreSQL regression, Angular checks and final-artifact authenticated browser journey pass. Built-in keyboard continuation confirms the one-row older page and heading focus with no console errors. Runtime fixture failures were corrected to obey existing invoice/milestone immutability without weakening guards or assertions. Evidence and limitations: `portalFinancePaging20261021` in [status.json](status.json). Nested invoice/receipt history beyond the decoder bound, full current-source release verification, human screen-reader review and provider/professional acceptance remain open. Earlier workflow-amendment, quotation-privacy and consolidation evidence is preserved in the [historical handoff](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md).

## Journal layout follow-up (2026-10-22)

- Styled draft Add/Remove actions using existing Material buttons; responsive metadata fields; moved balance feedback outside horizontal table scrolling.
- Focused scope-workspace specs: 19 passed; changed-file lint and diff check passed. Built-in browser: added/removed draft rows, focus returned to row 2 taxonomy, 390px viewport had no page overflow; console errors empty. No journal submitted.
- Full local suites not run. AC-09 human AT/native zoom evidence remains pending; release CI 39785630053 was still running when checked.

## Preview focus follow-up (2026-10-22)

Journal and sampling preview focus fixes passed focused specs, existing E2E, lint/build and built-in-browser checks. Later user focus and owner/preview fences are preserved. README and the Angular quality ledger archive record sources and limits. Fast CI passed; corrected-source release verification has passed Angular/build/contracts/Domain, with API/E2E running. Full matrix, native zoom and human AT remain open.

## Practice-management ledger-boundary regression (2026-10-18)

NR-11 is complete for the named three-book and reporting cases. Firm-ledger posting and the actual firm billing-invoice post leave client books unchanged; a client sales-invoice post creates no firm journal/posting and leaves the firm analytics aggregate unchanged; and the approved consolidation workflow preserves exact component TB rows, imported GL, native-journal evidence and package manifests. The original PostgreSQL cohort passed 3/3 on `a67b296f`, and the new firm-billing/client-report extensions passed 2/2 in the current focused run.

NR-12 remains partial. The crosswalk now maps five v2.2/host switches to existing off-path API and worker tests, their retained-state boundaries, and uncovered UI/configuration checks. The Angular work-queue spec also pins the explicit 403 unavailable message and confirms denied responses do not render summary/task cards (2/2 tests passed on the current slice). Built-in browser opened `/app/work`, but the local API currently has workflow tasks enabled and returned its normal view with one synthetic task; the route was read-only and produced no console errors or warnings. Therefore the disabled route itself was not browser-verified. The matrix is evidence mapping, not a fresh full regression or deployed-configuration acceptance. PM-01 remains gated on an approved, versioned jurisdictional source mapping; saved Audit Plan milestones and operational task deadlines are separate projections.

## Accounting and browser-journey repair (2026-10-18)

Commit `d5c5ca7ee436c86647c18115acae063a299273c1` fixes the saved purchase-invoice draft preview path: the preparer can supply and validate the approved AP role ID required by preview, and stale preview/assent state is cleared when the role or draft changes. The purchase-invoice draft query now uses a SQL-translatable latest-draft predicate. Search suggestions remain closed while loading, after results, or on errors; long audit-record textareas are constrained to their workspace. Several browser journeys and fixtures were aligned with the current accessible labels and current workflow contracts, without skipping tests.

Focused verification on the pushed source passed: purchase-invoice workflow Angular spec (9/9), workspace search Angular spec (4/4), Angular production build (existing `commercial/settings.scss` stylesheet budget warning), the sales invoice/supplier purchase-credit browser journey (1/1), and General Ledger acceptance journeys (2/2). The built-in browser smoke check used the current Angular dev server on port 4301 with the existing local API on port 5029 and synthetic data: workspace search returned two authorized Doha results, showed no overlapping suggestion list, and reported no console errors. It was read-only; no business records or access settings were changed. The long-running API/static bundle on port 5029 was left untouched.

The full unfiltered release workflow was dispatched against the exact pushed SHA; its run and result are recorded in `status.json`. This focused evidence does not replace full release verification or live-provider acceptance.

## PBC exact-version reviewer acceptance

Staff acceptance now requires the assigned, independently authorized reviewer to confirm the exact provider-verified upload ID, revision and content hash. The accepted request stores an immutable receipt binding that source to its request submission revision, provider identity/digest, reviewer and time. Database constraints validate the receipt against the request, received upload and completed transfer operation, and prevent rewriting the accepted decision or source. Legacy upload rows remain readable but cannot be accepted without matching current provider evidence.

Focused PBC Domain and PostgreSQL guard regressions passed, as did the PBC Angular contract spec; the API Release build and OpenAPI contract drift passed; Angular production build passed with the existing `settings.scss` budget warning. Commit `4bf84641` updates two PBC browser journeys to the current accessible control labels; both selected E2E journeys passed on that commit (2/2), including exact-version reviewer assent, receipt persistence and grant-revocation denial. Fast CI run `39093158735` passed on the same SHA: documentation health, Angular build/specs, .NET Release, architecture guards, EF drift, publication and OpenAPI. The built-in browser still has a stale local PBC bundle (lazy chunk 404), so manual browser acceptance remains unverified. Full release verification is not green: the earlier run on `9e2709b4` had Domain pass, API runner communication loss and E2E timeout with 24 failures before cancellation. Live-provider/business acceptance remains external. Exact evidence is in `status.json` under `targetedVerification.pbcExactVersionAcceptance20261018` and `testSuites.latestAttempt`.

## Statement content-review identity display (2026-10-23)

The screen shows persisted presentation/review IDs and full snapshot digest, with source currentness separate from historical approval. The server reuses exact package/layout/source validation and disables review of stale or unavailable source; Angular labels any retained approval historical. Returning a linked source preserves the original decision and draft hash. Focused PostgreSQL and Angular checks, targeted Release compilation and contract drift passed. Read-authority race checks still pass. Broader reporting, browser and professional acceptance remain separate; evidence is in `status.json`.

## Group report review-currentness guard (2026-10-23)

Group run creation, approval and current report readback now reuse the established package evidence checks after exact approved component-pin validation. A later returned review makes the report stale without rewriting its approved run or stored lines. A final group authorization check denies a grant revoked during the report read; group-only readers still cannot fetch raw client artifacts. The UI explains stale/unreviewed/unavailable report states instead of silently hiding the report. Focused PostgreSQL and Angular checks passed; exact results are in `status.json`. No new source-selection policy or final group export/archive workflow is implied. Current-source CI and broader release acceptance remain separate.

## Development dependency advisory repair (2026-10-23)

The Vite patch passed fresh audit, isolated install/build and focused session checks; servers were preserved. NuGet reported no findings. History scan false positives were source-reviewed without rewriting history or suppressing rules. Raw reports and remaining operational/release gates are in `status.json`.


Operational evidence guards reject invented scan/metric summaries; absent targets and approval remain unverified.

## API and Angular parity handoff archived 2026-10-23

## API and Angular bidirectional parity — active implementation

The [parity implementation tracker](auditsphere-execution-tracker-api-angular-parity-current.md) covers the attached API/Angular specification independently of the R2R and audit-programme task identifiers. Current source contains the authorization, decimal precision, workflow entry, recovery, supplier selection, portal refresh/history and consolidation ownership slices. Code presence is not final functional acceptance; current checks and remaining tasks are recorded under `apiAngularParity` in [status.json](status.json).

The allocation-reversal history route and correction handoff are connected. Review corrections restore API startup/contract generation, forward independent history pages, preserve exact normalized source ordering, clear scope-specific forms and review assent, and allow independent approval after reconciliation. Intercompany option-bound commands require reporting-period membership and reject negative revision sentinels. The ownership query now applies filtering/paging before its final DTO projection. That initial oversized-readiness limitation is superseded by the bounded scope continuation above: components and readiness have independent pages and complete counts. The existing ConsolidationQueryTests source exercises 501 members across six readiness pages; this reconciliation is static inspection, not a fresh test pass or full workflow acceptance.

Focused API, Domain and Angular checks passed for the affected slices; the emitted contract and drift check passed. Built-in browser smoke verified the synthetic overview, scope, external-pack history/source lines and recovered ownership panel. This was read-only browser coverage, not the complete submission/review/pinning journey or responsive/live-provider acceptance. Exact code snapshot, cohorts and limitations are under `apiAngularParity.localVerification` in [status.json](status.json). PR #46 and #47 are merged. Task statuses retain their remaining acceptance work.

GL upload checkpoint and earlier CI observations are preserved in the [historical handoff](auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md). Current accessibility and release evidence remains above and in [status.json](status.json).


## Subworktree integration (2026-10-23)

Unique GL and expense-account scope corrections were reconciled into the main checkout with signed-mail enrollment. The expense migration was regenerated after the mail migration; neither prior snapshot overwrote the other. Scoped foreign-parent refusal, ledger behavior, GL races and the authenticated mail workflow passed the combined focused checks. Older resolver work was already integrated; newer request-correlated diagnostics were retained. Original scratch sources/evidence are preserved locally for cleanup. Exact verification and deferred CI/live gates remain in `status.json`; no whole-project completion or deployment is claimed.


## Mail scope evidence projection (2026-10-23)

Role-only rows cannot verify mail or reuse a verification timestamp. The Graph worker now requires signed source/model/tenant/sender/control evidence, current connection/consent, and model-specific token claims before dispatch; authority is rechecked after evidence I/O. Focused tests and compilation passed; Compose uses immutable sender IDs. The newer enrollment section supersedes the earlier receipt/API metadata gap; broader readiness, migration/rollout and live acceptance remain open. Evidence is in `status.json`.


## Signed Legacy mail evidence enrollment before recovery (2026-10-23)

Firm-wide administrators can enroll exact public CMS evidence through the authenticated API/Angular workflow. Append-only receipts are replay-safe, rechecked against current deployment/connection/consent and expiry, and read by the Graph mail worker only when explicitly configured as `ENROLLED`; `FILE` remains the default. Generic role-only rows never qualify. The CLI reads safe receipt metadata. Downgrade refuses to discard retained bytes. Focused evidence and source limits are in `status.json`; issuer tooling, broader runtime readiness, interactive/current-source full CI, live provider and rollout acceptance remain open. No live effect or deployment was performed.
