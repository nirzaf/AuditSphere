# AuditSphere — v3 UI design: screen-to-code implementation matrix

**Status:** PROPOSED (design-to-code mapping; not acceptance evidence)
**Code baseline:** `master@20706b4f` · prepared 19 October 2026
**Design source:** [`auditsphere-ui-design-v3-code-aligned.pdf`](../auditsphere-ui-design-v3-code-aligned.pdf) (19 screens) and `auditsphere-ui-design-full.figcraft`, page "v3 Code-aligned (AS-UX-001)"
**Related:** [AS-UX-001 approved stories](../execution/auditsphere-execution-user-stories-as-ux-001-figma-angular-parity-approved.md) · [AS-UX-001 tracker](../execution/auditsphere-execution-tracker-as-ux-001-figma-angular-parity-current.md)

## How to read this matrix

Each screen lists the existing Angular route and component, the API endpoints it calls, the server fields that back the design, the permission flags that gate its actions, and the UI changes that remain.

**Method.** Static source scan of `app.routes.ts`, the feature components and their contract files (API URLs, decoder fields, `can*` flags), and `src/AuditSphereOps.Api/Ui/UiEndpoints.*.cs` route templates, with targeted reads of Application records. No application was run. A listed field or flag shows that the contract exists, not that the behaviour was accepted.

**Implementation classes**

| Class | Meaning |
|---|---|
| **R** — Restyle | Data and actions already exist. Apply the v3 visual design to the existing component. |
| **C** — Compose | The design combines data that today lives on more than one route. Either link to those routes or compose existing endpoints; do not duplicate their logic. |
| **P** — Proposed | The design shows a field, projection or action that the current contract does not provide. It needs an approved story and a server change before the UI can show it. |

**Rules for implementers**

- Keep authorization in Application commands and queries. The `can*` flags below are server-projected presentation hints only.
- Reuse the existing component and its contract file. Change the template and styles; do not create a second component for the same route.
- Where a row is marked **P**, show nothing or a neutral placeholder until the server field exists. Never compute it in Angular from partial data.
- Apply the shell tokens from AS-UX-001 (`--as-shell-dark #0f172a`, `--as-canvas #f1f5f9`, `--as-action-primary #2563eb`, 4rem header, 16rem sidebar) in `app.html`, `app.scss` and `core/workspace-navigation.ts`.

## Summary

| # | Screen | Route | Angular component | Primary API | Class |
|---|---|---|---|---|---|
| 01 | Engagement workspace | `app/engagements/:id` | `features/engagements/engagement.ts` · `EngagementDetail` | `GET /api/ui/engagements/{id}` · `/lifecycle` | R · C · P |
| 02 | Engagement activation | `app/engagements/:id/activation` | `features/engagements/activation.ts` · `EngagementActivationReview` | `GET …/activation-review` · `POST …/activate` | R · P |
| 03 | My work | `app/work` | `features/practice/work-queue.ts` · `WorkflowWorkQueue` | `GET /api/ui/work/tasks` · `/work/reviews` | R |
| 04 | Audit programme library | `app/audit/library` | `features/audit/library.ts` · `AuditProgramLibrary` | `GET /api/ui/audit/library` | R |
| 05 | Workpaper | `app/audit/workpapers/:id` | `features/audit/workpaper.ts` · `WorkpaperEditor` | `GET /api/ui/audit/workpapers/{id}` | R · C |
| 06 | Accounting workspace | `app/accounting` | `features/accounting/workspace.ts` · `AccountingWorkspace` | `GET /api/ui/accounting/clients/{id}` | R · C |
| 07 | Invoice | `app/practice/invoices/:id` | `features/finance/invoice.ts` · `InvoiceDetail` | `GET /api/ui/finance/invoices/{id}` | R · P |
| 08 | Firm books | `app/finance` · `app/finance/books` | `features/finance/ledger.ts` · `FirmLedger`; `features/finance/books.ts` · `FirmBooks` | `GET /api/ui/finance` · `/finance/books` | R · C |
| 09 | Group consolidation | `app/consolidation/scopes/:id` | `features/consolidation/scope-workspace.ts` · `ConsolidationScopeWorkspace` | `GET /api/ui/consolidation/scopes/{id}` | R · P |
| 10 | Time & analytics | `app/practice/analytics` | `features/practice/analytics.ts` · `PracticeAnalytics` | `GET /api/ui/practice/analytics` | R |
| 11 | Operations | `app/operations` | `features/admin/operations.ts` · `Operations` | `GET /api/ui/operations` · `/operations/discovery` | R |
| 12 | Users & access | `app/administration/users` | `features/admin/access.ts` · `UserAccess` | `GET /api/ui/administration/access` | R · C |
| 13 | Client portal | `portal` | `features/portal/home.ts` · `ClientPortalHome` | `GET /api/ui/portal` | R · P |
| 14 | PBC requests | `app/engagements/:id/pbc` | `features/engagements/pbc.ts` · `PbcInbox` | `GET /api/ui/engagements/{id}/pbc` | R |
| 15 | Engagement completion | `app/engagements/:id/completion` | `features/audit/completion.ts` · `EngagementCompletion` | `GET /api/ui/engagements/{id}/completion` | R · C |
| 16 | Financial statements | `app/engagements/:id/statements` | `features/engagements/statements.ts` · `StatementDrillDown` | `GET /api/ui/engagements/{id}/statements` | C · P |
| 17 | Technical library | `app/library` · `app/library/:id` | `features/library/library.ts` · `TechnicalLibrary` | `GET /api/ui/library` | R |
| 18 | Portfolio | `app` | `features/portfolio/portfolio.ts` · `Portfolio` | `GET /api/ui/portfolio/workspace` | R · P |
| 19 | Access denied (state) | any staff route | `core/api.ts` 403 handling · `core/ui.ts` `audit-state` | `ApiError { code, message, correlationId }` | R |

All API paths below are relative to `/api/ui`.

---

## 01 · Engagement workspace — R · C · P

- **Route / component:** `app/engagements/:id` → `features/engagements/engagement.ts` (`EngagementDetail`), with `engagement-lifecycle.ts`, `engagement-contracts.ts`, `engagement-lifecycle-contracts.ts`, `engagement-onboarding-readiness.ts`, `engagement-communications.ts`.
- **Endpoints:**
  - `GET /engagements/{id}` and `GET /engagements/{id}/lifecycle` (seven-phase ribbon).
  - `GET /engagements/{id}/workflow`, `/workflow/tasks`, `/onboarding-readiness`, `/activity`, `/milestones`, `/communications`.
  - `POST /engagements/{id}/task-plan/{action}` and receipts lookup.
  - Procedure table data: `GET /engagements/{id}/fieldwork`, owned by `app/engagements/:id/audit-fieldwork` (`AuditFieldwork`).
  - Manager handover readiness: `GET /engagements/{id}/manager-review`.
- **Server fields behind the design:**
  - Lifecycle: `currentStatus`, `canonicalIndex`, `blockedReasons`.
  - Activation history: `activation`, `advancePayment.allocated`.
  - Archive: `archive.archiveDeadlineDate`, `complianceCountdownDays`.
  - Tasks: assignee, due date and dependency fields from the workflow payload.
- **Actions and flags:** `canActivate`, `canCreatePbcDrafts`, `canExecute`, `canGenerateLetter`, `canPrepareAccounting`, `canViewClientProfile`.
- **Remaining UI changes:**
  - Restyle the lifecycle ribbon as the seven-phase stepper. Phases come only from the lifecycle projection; do not derive them in Angular.
  - **C:** show the procedure summary from `GET …/fieldwork`, or link to `audit-fieldwork`.
  - **P:** "Request Manager review" — there is a `GET …/manager-review` summary but no handover command. Show readiness as text until a command is approved.
  - Split "Current gates" (`blockedReasons`) from "History" (retained activation).

## 02 · Engagement activation — R · P

- **Route / component:** `app/engagements/:id/activation` → `features/engagements/activation.ts` (`EngagementActivationReview`) and `activation-contracts.ts`.
- **Endpoints:** `GET /engagements/{id}/activation-review`, `POST /engagements/{id}/activate`.
- **Server fields behind the design** (`EngagementActivationState`):
  - `Eligible`, `Blockers[]`, `ActiveHolds`, `Decision`, `ReviewBasis`.
  - `Activation` (`RetainedActivationView`).
  - `Commercial` (`ActivationCommercialView`: quotation version and revision, approval IDs, dispatch ID, offer SHA-256, response ID, accepted time).
  - Preview: `EngagementActivationPreview.RequestHash`.
- **Actions and flags:** activation is enabled by `Eligible` plus an empty `Blockers` list; the server re-checks both keys and the advance at commit.
- **Remaining UI changes:**
  - The Angular decoder does not yet read `Commercial`. Add it to `activation-contracts.ts` so the Key 1 card shows real quotation and dispatch identities.
  - For an already active engagement, show `RetainedActivationView` and no activation button.
  - **P:** the Key 2 and advance cards need explicit current risk-approval and net-advance fields if the server does not already expose them through `Blockers`. Until then, show the blocker list.

## 03 · My work — R

- **Route / component:** `app/work` → `features/practice/work-queue.ts` (`WorkflowWorkQueue`) and `work-queue-views.ts`.
- **Endpoints:**
  - `GET /work/tasks`, `GET /work/reviews`, task activity.
  - `POST …/workflow/tasks/{taskId}/complete`, `…/due-date`, `…/{action}`.
- **Server fields behind the design:**
  - `tasks[]`: `procedureTitle`, `dueDate`, `assigneeDisplayName`, `pbcState`, `reviewState`, `taskRevision`.
  - `reviews[]`, `counts`, `asOfDateUtc`.
- **Actions and flags:** `canAssign`, `canChangeDueDate`, `canComplete`, `canRetryAssignment`.
- **Remaining UI changes:**
  - Restyle the queues.
  - KPI cards must use `counts` only. Show "Overdue" only if it is a server count; otherwise omit it.
  - Build the "Waiting for client" panel from `pbcState`.

## 04 · Audit programme library — R

- **Route / component:** `app/audit/library` → `features/audit/library.ts` (`AuditProgramLibrary`).
- **Endpoints:**
  - `GET /audit/library`, `GET /audit/library/{versionId}/sections/{section}`.
  - `POST /audit/library/versions/prepare`, `POST /audit/library/versions/{versionId}/publish`.
- **Server fields behind the design:**
  - `versions[]`: `version`, `status` (DRAFT, PUBLISHED, RETIRED), `contentDigest`, `sourceHash`, `procedureCount`, `sectionCount`, `approvedAt`, `publicationReviewReference`.
  - `sections[]`.
  - Items: `sourceProcedureId`, `sourceWording`, `assertions`, `expectedEvidence`, `applicabilityCondition`.
- **Actions and flags:** `canPrepare`, `canPublish`.
- **Remaining UI changes:**
  - Restyle.
  - "View catalogue" uses the sections endpoint.
  - Show `publicationReviewReference` beside the version status.
  - Keep engagement tailoring off this screen.

## 05 · Workpaper — R · C

- **Route / component:** `app/audit/workpapers/:id` → `features/audit/workpaper.ts` (`WorkpaperEditor`), with `workpaper-command-state.ts`, `-review.ts` and `-contracts.ts`.
- **Endpoints:**
  - `GET /audit/workpapers/{id}`.
  - `POST /audit/workpapers/{id}/commands` (client side), backed by `…/draft`, `…/draft/discard` and `…/submit`.
- **Server fields behind the design:**
  - `procedure` (`procedureId`, `linkedProcedureTitle`), `objective`, `conclusion`.
  - `evidence[]`: `fileName`, `contentSha256`, `linkedAt`.
  - `submissions[]`: `submittedAt`.
  - `draft`: `lastSavedAt`, `lastSaveId`.
  - `lifecycle`, `blockers`.
- **Actions and flags:** `canSaveDraft`, `canDiscardDraft`, `canSubmit`, `canPrepare`, `canProceed`.
- **Remaining UI changes:**
  - Label submissions as result revisions and show the evidence frozen with each submission.
  - **C:** reviewer identity and review revision come from the fieldwork and review-note owners. Link or compose them; do not add review state to the workpaper contract.

## 06 · Accounting workspace (external-source mode) — R · C

- **Route / component:** `app/accounting` → `features/accounting/workspace.ts` (`AccountingWorkspace`). It composes `lifecycle`, `charts`, `counterparties`, `operational-journals`, `operational-opening-balances`, purchase and sales invoice workflows, and `open-item-allocations` for native bookkeeping.
- **Endpoints:**
  - `GET /accounting/clients`, `GET /accounting/clients/{id}`, `GET /accounting/task-owners`.
  - `POST /accounting/clients/{id}/profile`, `/books`, `/periods`, `/rollforward`, `/restatements`.
- **Server fields behind the design:**
  - `profile`: `sourceMode` (EXTERNAL_SOURCE or NATIVE_BOOKKEEPING), `currency`, `fiscalMonth`, `fiscalDay`, `jurisdiction`, `status`, `revision`.
  - `books`, `periods`, `openingBridges`, `amendments`.
- **Actions and flags:** `canReviewOpening`, plus the flags of each composed sub-component.
- **Remaining UI changes:**
  - **C:** the design's KPI cards (trial balance revision, mapping version, adjustment plans, audit differences) and the work queue come from the engagement-scoped routes, not this payload:
    - `app/engagements/:id/tb-intake`
    - `app/accounting/mappings`
    - `app/accounting/adjustment-plans`
    - `app/accounting/differences`
    - `app/accounting/evidence`

    Compose links or summary calls to those routes.
  - Native-bookkeeping mode needs its own design treatment. This screen covers external-source mode only.

## 07 · Invoice — R · P

- **Route / component:** `app/practice/invoices/:id` → `features/finance/invoice.ts` (`InvoiceDetail`).
- **Endpoints:**
  - `GET /finance/invoices/{id}`, with paging for credit notes and receipts.
  - `POST /finance/invoices/{id}/{action}`, `/credit-notes`, `/payment-terms`.
  - `POST /finance/receipts/{id}/allocations`, `/finance/allocations/{id}/reversals`, `/finance/allocation-reversals/{id}/review`, `/finance/invoice-payment-terms/{revisionId}/review`.
  - `GET /finance/commands/receipts/{requestId}`.
- **Server fields behind the design:**
  - `invoiceNumber`, `currency`, `postedAt`, `dueDate`.
  - `lines[]` (`description`, `quantity`, `lineTotal`).
  - `allocations[]`, `appliedAfterReversals`, `outstanding`, `credited`, `creditNotes[]`, `paymentTerms[]`.
- **Actions and flags:** `canApproveInvoice`, `canPostInvoice`, `canSendInvoice`, `canIssueCreditNote`, `canSubmitPaymentTerms`, `canReviewPaymentTerms`, `canRequestReversal`, `canReviewReversal`, `canAct`.
- **Remaining UI changes:**
  - Map "Net applied after reversals" to `appliedAfterReversals`.
  - **P:** "Outstanding cash advance" is an engagement-level gate, not an invoice field. Link to the engagement instead.
  - **P:** there is no tax field in the invoice decoder. Remove the tax caption until a tax contract exists.
  - Send is a status transition today, not evidenced dispatch (gap AUD-WF-03). Do not show dispatch evidence.

## 08 · Firm books — R · C

- **Route / component:**
  - `app/finance` → `features/finance/ledger.ts` (`FirmLedger`).
  - `app/finance/books` → `features/finance/books.ts` (`FirmBooks`).
  - Related: `app/finance/receivables-aging`, `app/finance/end-of-service`.
- **Endpoints:**
  - `GET /finance`.
  - `POST /finance/accounts`, `/finance/journals`, `/finance/journals/{id}/submit|review|post|evidence`, `/finance/periods/{id}/close|reopen`, `/finance/postings/{id}/reverse`.
  - `GET /finance/books`, `/finance/books/trial-balance`.
- **Server fields behind the design:**
  - `accounts[]`, `periods[]`, `postings[]`.
  - `journals[]`: `journalNumber`, `lines`, `postedAt`, `approvedAt`, `revision`.
- **Actions and flags:** `canCreateJournals`, `canReviewJournals`, `canPostJournals`, `canReversePostings`, `canClosePeriod`, `canReopenPeriod`, `canCreateSetup`.
- **Remaining UI changes:**
  - **C:** the KPI cards and ageing panel come from `receivables-aging` and `end-of-service`. Compose or link them.
  - Keep the end-of-service figure as a posted balance with its evidence.

## 09 · Group consolidation — R · P

- **Route / component:** `app/consolidation/scopes/:id` → `features/consolidation/scope-workspace.ts` (`ConsolidationScopeWorkspace`), with `scope-contracts.ts`, `ownership.ts`, `scope-intercompany-state.ts`, `scope-journal-state.ts`, `external-packs.ts`.
- **Endpoints:**
  - `GET /consolidation/scopes/{id}`, paged for components, runs, journals, readiness and eligible packages.
  - `POST /consolidation/scopes/{id}/approve`, `/scopes/{id}/run`, `/components/{id}/approve`, `/runs/{id}/approve`.
  - Intercompany match endpoints.
- **Server fields behind the design:**
  - `components[]`: `clientLegalName`, `currency`, `currencyCompatible`, `mappingVersion`, `hasApprovedComponent`.
  - `groupRevision`, `runs[]` (`calculationHash`), `journals[]`, readiness rows.
- **Actions and flags:** `canPrepare`, `canSubmit`, `canApprove`, `canSubmitIntercompanyMatch`, `canApproveIntercompanyMatch`.
- **Remaining UI changes:**
  - **P:** "Stale · newer source" needs a component-currentness field (gap AUD-FIN-08).
  - **P:** "Build group package" has no route yet (gap AUD-FIN-11). Hide the button until both exist.

## 10 · Time & analytics — R

- **Route / component:** `app/practice/analytics` → `features/practice/analytics.ts` (`PracticeAnalytics`).
- **Endpoints:** `GET /practice/analytics`, with engagement page size up to 100.
- **Server fields behind the design:**
  - `capacityMinutes`, `chargeableMinutes`, `actualMinutes`, `budgetMinutes`, `billed`, `collected`, `collectionPercent`.
  - Completion counts, `departments[]`, `engagements[]`, `definitions`.
- **Actions and flags:** read-only.
- **Remaining UI changes:**
  - Use the server `definitions` for utilisation and realisation captions.
  - The "By team" table comes from `departments[]`.
  - Unbilled time stays off this screen. It is `GET /finance/unbilled-time`, restricted to FinanceManager and FinanceReviewer.

## 11 · Operations — R

- **Route / component:** `app/operations` → `features/admin/operations.ts` (`Operations`), with `operations-contracts.ts` and `operations-command-*`.
- **Endpoints:**
  - `GET /operations`, `GET /operations/discovery` (older unresolved operations).
  - `GET /operations/commands/state`, `GET /operations/commands/receipts/{requestId}`.
  - `POST /operations/commands/preview`, `/operations/commands/actions`, `/operations/lift-quarantine`.
- **Server fields behind the design:** `id`, `kind`, `attemptCount`, `nextAttemptAt`, `errorCode`, `correlationId`, `hasResultEvidence`, `disposition`, `blockers`.
- **Actions and flags:** `canRetry`, `canCancel`, `canLiftQuarantine`, `canApproveRecoveryRestart`, `canPrepare`, `canProceed`, `canPreviousDiscovery`.
- **Remaining UI changes:**
  - Restyle.
  - KPI cards must come from server counts or the discovery result, not a client-side count of one page.

## 12 · Users & access — R · C

- **Route / component:**
  - `app/administration/users` → `features/admin/access.ts` (`UserAccess`) and `access-contracts.ts`.
  - Microsoft 365: `app/administration/microsoft365/tenant-connection` → `features/admin/tenant.ts` (`TenantConnection`).
- **Endpoints:**
  - `GET /administration/access`.
  - `POST /administration/access/preview`, `/assign`, `/assign-invitation`, `/revoke`, `/bind-manual`, invitation endpoints.
  - `GET /administration/microsoft365`.
- **Server fields behind the design:** `displayName`, `accountType`, `currentAccess`, `grantHistory`, `expiresAt`, `invitationStatus`, `directoryStatus`, `independenceImpact`, `blockingReasons`.
- **Actions and flags:** server preview and `blockingReasons`. There are no client `can*` flags.
- **Remaining UI changes:**
  - **C:** the tenant-check card must show the environment, capability and time taken from the Microsoft 365 payload, never a generic "verified".
  - Derive "Expiring grants" from `expiresAt` within the server-returned page only, or request a server count.

## 13 · Client portal — R · P

- **Route / component:** `portal` → `features/portal/home.ts` (`ClientPortalHome`), with `documents.ts`, `finance.ts`, `journals.ts`, `report-action.ts`, `branding.ts`, `contracts.ts`.
- **Endpoints:** `GET /portal` (paged), `POST /portal/first-sign-in`.
- **Server fields behind the design:**
  - `requests[]`: `objective`, `state`, `transferState`, `requestedFormat`, `instructions`.
  - `uploads[]`, `packages[]`, `conversation`, `delegations`, `pendingOnboarding`, `asOfDateUtc`.
- **Actions and flags:** `canWrite`, `canComplete`, `canProvideDocument`, `canDelegate`.
- **Remaining UI changes:**
  - Use the client-only shell. The sidebar shows only "Client portal" (matches `WorkspaceNavigation` for client sessions).
  - "Messages" maps to `conversation`.
  - **P:** the upload limit should come from the server rather than be hard-coded. Current policy is 250 MiB per PBC file and 10 MiB for a signed representation letter. Show text only after a policy field exists, or link to help.

## 14 · PBC requests — R

- **Route / component:** `app/engagements/:id/pbc` → `features/engagements/pbc.ts` (`PbcInbox`).
- **Endpoints:**
  - `GET` and `POST /engagements/{id}/pbc`.
  - `POST /pbc-drafts`, `/pbc-requests/{id}/state`, `/pbc-requests/{id}/more-files`, `/pbc-uploads/{id}/complete`.
- **Server fields behind the design:**
  - `requests[]`: `area`, `objective`, `state`, `dueDate`, `requestRevision`, `acceptance.acceptedAt`, `reviewerUserId`.
  - `intents[]`: `fileName`, `byteCount`, `contentSha256Hex`, `providerIdentity`, `providerReceiptDigest`, `transferState`.
  - `timeline`.
- **Actions and flags:** `canAccept`, `canRequestMore`, `canStartReview`.
- **Remaining UI changes:**
  - Restyle.
  - Keep "Received" (`transferState` and provider receipt) and "Accepted" (`acceptance`) as separate columns and counts.

## 15 · Engagement completion — R · C

- **Route / component:** `app/engagements/:id/completion` → `features/audit/completion.ts` (`EngagementCompletion`).
- **Endpoints:**
  - `GET /engagements/{id}/completion`, `GET …/fieldwork`, `GET …/early-lock/readiness`.
  - `POST …/completion/srm|clearance|opinion|reports|bundle|release-candidate|workprogram-approval`.
  - `/deliverables/{id}/signature`, `/representation-scans/…`, `…/holding-letter/dispatch`.
- **Server fields behind the design:**
  - `clearance.clearedAt`.
  - `release`: `candidateStatus`, `checkpointState`, `checkpointMatchesCandidate`, `checkpointReadBackMatchesManifest`, `issueState`, `releaseManifestDigest`.
  - `bundles.blockers`, `deliverables[]`, `holdingLetter`, `blockers`.
- **Actions and flags:** `canApprove`, `canApproveCurrentWorkprograms`, `canManageFieldwork`, `canPrepareRelease`.
- **Remaining UI changes:**
  - Map each release-gate row to the fields above.
  - Keep "Release" disabled unless `canPrepareRelease` is true and the gates are satisfied.
  - **C:** freeze timing (`ReportSignedAt`, `DueAt`, local and external state) lives with records archives (`app/records/archives/:id`, `FileFreezeService`). Link or compose it.

## 16 · Financial statements — C · P

- **Route / component:** `app/engagements/:id/statements` → `features/engagements/statements.ts` (`StatementDrillDown`), with `statement-contracts.ts` and `tb-source.ts`.
- **Endpoints:**
  - `GET /engagements/{id}/statements`, `/statements/workspace`, `/statements/contributions`, `/statements/split`.
  - `POST /engagements/{id}/statements/export`.
  - Procedure link: `POST /procedures/{id}/review`.
- **Server fields behind the design:**
  - `financialPosition`, `profitOrLoss`.
  - `lines[]` with `priorAmount` and `variance`.
  - `procedures[]` with `currentResult` and `reviewedAt`.
  - `mappingVersion`, `datasetRevision`.
- **Actions and flags:** none on this screen. Review actions belong to the procedure and package owners.
- **Remaining UI changes:**
  - **C:** changes in equity, cash flows, notes and package approval state are **not** in this contract. They live in the financial package and presentation owners:
    - `app/engagements/:id/financial-package`
    - `app/accounting/packages/:id`
    - `features/accounting/presentation.ts`

    The v3 screen merges these. Implement it as a composition or as links.
  - **P:** the "Reviewed object / content revision" column needs a per-statement review field from the presentation contract. Verify before display.

## 17 · Technical library — R

- **Route / component:** `app/library` and `app/library/:id` → `features/library/library.ts` (`TechnicalLibrary`).
- **Endpoints:**
  - `GET /library`, `/library/search`, `/library/{id}`.
  - `POST /library`, `/library/{id}/versions`, `/library/versions/{versionId}/publish`.
- **Server fields behind the design:** `code`, `title`, `category`, `audience`, `status`, `publishedVersion`, `hasDraft`, `history[]`, `effectiveFrom`, `contentSha256`.
- **Actions and flags:** no client flags. The server enforces authority.
- **Remaining UI changes:**
  - Restyle.
  - Use `category` and `audience`. There is no "owner" field.
  - Version history comes from `history[]`.

## 18 · Portfolio — R · P

- **Route / component:** `app` → `features/portfolio/portfolio.ts` (`Portfolio`) and `portfolio-contracts.ts`.
- **Endpoints:** `GET /portfolio/workspace`, `POST /portfolio/export`.
- **Server fields behind the design:**
  - `metrics`: `clients`, `engagements`, `openHolds`, `pendingOperations`, `readyCandidates`, `issuedReleases` (`PortfolioMetrics`).
  - `clients` page, `candidates[]`, `packages[]`, `hasActiveMicrosoftConfiguration`.
- **Actions and flags:** export and navigation only.
- **Remaining UI changes:**
  - The six KPI cards match `PortfolioMetrics`.
  - **P:** the engagement table's lifecycle-phase and per-row hold columns are not in `PortfolioWorkspace`. It returns a client page, not engagement rows with lifecycle state. A new authorized projection is needed.
  - **P:** "Waiting for client", "Awaiting my review" and "Blocked gates" also need a new authorized projection.

## 19 · Access denied (state) — R

- **Route / component:** no dedicated route.
  - `core/api.ts` maps 403 to a "forbidden" resource state ("Not available in your current scope.").
  - `core/ui.ts` provides the shared `audit-state` component.
- **Contract:** errors use `ApiError { code, message, correlationId }` (`src/AuditSphereOps.Api/Contracts/ApiContract.cs`).
- **Remaining UI changes:**
  - Render the v3 generic state through `audit-state`: "Engagement unavailable", no protected names, and a back-to-portfolio action.
  - "Copy reference ID" copies `correlationId` only.

---

## Cross-cutting work (all screens)

| Item | Owner files | Notes |
|---|---|---|
| Dark shell, 4rem header, 16rem sidebar, slate-100 canvas | `app.html`, `app.scss`, `core/workspace-navigation.ts`, `core/navigation.ts` | AS-UX-001 UX-01/UX-02. Preserve session, sign-out and pending-outcome behaviour. |
| Engagement context in header | `app.html` | Display only. A global engagement selector is a new feature, not restyling. |
| Status badges, page header, notices | `core/ui.ts` (`audit-status`, `audit-page-header`, `audit-command-message`) | Restyle the shared components once instead of per screen. |
| Disabled actions with reason | Each feature component | Bind `disabled` and the explanation text to the server `can*` flag and blockers. |
| Responsive, keyboard and accessibility | All | Not covered by the PDF. Required by the AS-UX-001 tracker before acceptance. |

## Proposed server work collected from this matrix

| ID | Screen | Needed | Related gap |
|---|---|---|---|
| P-01 | 01 | Manager-review handover command, or explicit confirmation that readiness is display-only | — |
| P-02 | 02 | Decode `ActivationCommercialView`; expose current risk-approval and net-advance fields if not already in blockers | AUD-WF-01 |
| P-03 | 07 | Tax fields on invoices before any tax display; evidenced invoice dispatch | AUD-WF-03 |
| P-04 | 09 | Component-currentness field; group financial package route | AUD-FIN-08, AUD-FIN-11 |
| P-05 | 13 | Server-provided upload-policy field per workflow | — |
| P-06 | 16 | Per-statement review identity in the presentation contract | AUD-FIN-04 |
| P-07 | 18 | Authorized engagement-row projection with lifecycle phase and holds; waiting/review/blocked counts | — |

## Verification boundary

- Static mapping only. No build, test or browser run was performed for this document.
- Field lists come from decoder and contract files and may include nested names. Confirm exact paths against the contract file before binding a template.
- Routes and endpoints were read at `master@20706b4f`. Re-check them if the contracts change.
