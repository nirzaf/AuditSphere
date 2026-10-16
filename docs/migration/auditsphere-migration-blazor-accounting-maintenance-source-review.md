# AuditSphere — Blazor Accounting Workspace and Period Maintenance Source Review

**Status: `PARTIAL_REVIEWED`.** This review covers six rows from the pinned Blazor source/action inventory. It compares their actions with the current Angular/API/Application owners and the focused PostgreSQL-backed journeys. It does not establish complete accounting parity or authorize Blazor removal.

## Review boundary

The six Blazor files below were re-hashed against inventory snapshot `eb94ae5073558ec7192ddb5dfd4e24cecd7c4b39`; all six hashes match. Angular pages call same-origin API endpoints. API handlers compose the Application queries and services listed here; no new business mutation is performed from Angular or Razor directly against the database. Test fixtures use isolated PostgreSQL databases and synthetic identities.

## Source/action crosswalk

| Blazor source/action | Angular/API/Application owner and evidence | Disposition and open evidence |
|---|---|---|
| `Pages/AccountingEvidence.razor`: refresh the scoped evidence queue, inspect status/counts and follow exact evidence links; the legacy table offers 10/25/50 rows per page. | `features/accounting/evidence.ts`; `GET /api/ui/accounting/evidence`; `AccountingEvidenceQueueQuery`. `evidence.spec.ts` verifies the 10/25/50 choices, default 25, page changes and return to page one. `AngularAccountingEvidenceQueueJourneyTests.PartnerEvidenceQueueShowsOnlyTheAssignedClient` and `.ScopedEvidenceCountsRemainIsolatedAndDisappearAfterEpochLoss` verify client isolation, scoped counts and clearing after session-epoch loss. | **Partial.** Queue scope, count isolation, epoch-loss clearing and pager choices are covered. The complete evidence-kind/action matrix, detail/procedure transitions, provider/query failures, stale refresh behavior and full role/scope matrix remain open. |
| `Pages/AccountingPeriod.razor`: refresh an exact reporting-period record and expose package, mapping, PBC and task handoffs. | `features/accounting/period.ts`; `GET /api/ui/accounting/periods/{id}`; `AccountingPeriodRecordQuery`. `AngularFinancialArtifactParityJourneyTests.ReopenedPeriodShowsNewRevisionWithoutReplacingValidatedPackage` and `AngularAccountingScopeParityJourneyTests.MismatchedAndOtherRoleGrantsDoNotExpandAngularAccountingWorkspace` cover revision lineage and role/scope denial. | **Partial.** The exact-record query reauthorizes the client scope and does not invent missing owners or due dates. Current specs cover populated and absent handoffs, wrong IDs, route ABA, denial and session clearing. Persisted owner/due/state and exact-link browser assertions plus transient-refresh/retry coverage are added but await current results. |
| `Pages/AccountingRecords.razor`: refresh three scoped mapping, journal and difference queues, follow exact detail links and change the page size (10/25/50). | `features/accounting/records.ts`; `GET /api/ui/accounting/mappings`, `/journals`, `/differences`; `AccountingRecordsQuery`. `records.spec.ts` verifies 10/25/50, default 25, page reset and row counts. `AngularFinancialArtifactParityJourneyTests.AccountingQueuesAndDetailsClearAfterRouteChangesAndGrantRevocation` and `SiblingClientIsolationJourneyTests.SiblingClientDataNeverChangesWhatAClientScopedUserSees` cover detail clearing, revocation and sibling-client isolation. | **Partial.** Queue projections are bounded and scope-checked; the three record queues share the tested pager behavior. The automatic-reload page reset is corrected for all three queues. New real-resource specs cover shrinking authorized results and transient-failure clearing/retry; current runtime results and scoped browser reconciliation remain pending. |
| `Pages/AccountingWorkspace.razor`: search and page authorized clients, inspect and revise a client accounting profile, reporting periods, books, mappings, task ownership and opening-balance bridge state. | `features/accounting/workspace.ts`; `GET /api/ui/accounting/clients` and `/api/ui/accounting/clients/{id}`; `AccountingWorkspaceQuery`. `AngularAccountingJourneyTests.ReviewedSetupPersistsProfilePeriodAndBook`, `AngularAccountingScopeParityJourneyTests.MismatchedAndOtherRoleGrantsDoNotExpandAngularAccountingWorkspace`, and `AngularAccountingTaskOwnerScopeJourneyTests.AccountingWorkspaceShowsOnlyOwnersInAssignedEngagementAndClearsAfterRevocation` cover reviewed setup, scope and assigned-engagement task-owner projection. | **Partial.** Search, bounded paging, client selection, setup, and task-owner scope have focused evidence. The full profile/period/book/chart/bridge action matrix, pagination boundaries, stale selection and all failure/recovery paths remain open. |
| `Pages/PeriodRestatements.razor`: load closed-period package choices, create an exact restatement request, independently approve and inspect its history. | `features/accounting/restatements.ts`, `ReviewedPeriodMaintenance`, `PeriodMaintenanceWorkspace` and scoped accounting services. `AngularPeriodMaintenanceJourneyTests.RestatementWorkbenchCreatesAndIndependentlyApprovesImmutableLineage` covers exact package identities/hashes, unchanged source packages, independent approval, refreshed history and dropped-response recovery. Page specs cover scope cleanup, stale reads, exact intent and retained requests. | **PARITY_VERIFIED — pinned source.** The register identifies the verified source/test commit. Broader accounting workspace, human/global acceptance and retirement remain open. |
| `Pages/PeriodRollforward.razor`: load a closed prior period and validated package, retain displayed defaults/history, record source evidence and opening amounts, and create a draft next period with an unapproved bridge. | `features/accounting/rollforward.ts`, `ReviewedPeriodMaintenance`, `PeriodMaintenanceWorkspace` and `ClientAccountingService.RollForwardPeriodAsync`. The dedicated journey covers external evidence, exact validated-package/hash selection through preview/dispatch/bridge/receipt, one publication, defaults and refreshed history; direct command assertions refuse mismatched hash/package without side effects. Dropped-response recovery and page-spec scope/draft fences are covered. | **PARITY_VERIFIED — pinned source.** Bridge approval is a separate accounting-workspace action. Human/global acceptance and retirement remain open. |

## Verification evidence

- Angular unit suite: **482/482 passed across 94 files**, including the evidence and records pagination cases.
- Angular production build: passed with the existing initial-bundle warning, **506.60 kB against a 500 kB warning budget**.
- Focused PostgreSQL-backed Release E2E cohort: **21/21 passed, zero failed or skipped**, including the two new period-maintenance journeys; exact command and coverage are in `status.json` under `angularAccountingMaintenanceSourceReview`.
- Built-in browser: after reloading the authenticated Development Mappings page, the rebuilt UI showed **25** selected with 10/25/50 available. The queue was empty for the current identity and the browser console had no errors or warnings; this was read-only.

## Result and boundary

The roll-forward and restatement source rows are `PARITY_VERIFIED`; other reviewed rows remain `PARTIAL`. Current source/action counts and dispositions are maintained in the migration register and `status.json`; this review does not promote a row by route existence alone. Twenty-eight supporting files remain open. The removed E2E-method crosswalk remains 82/82; that separate crosswalk does not close AS-PAR-002 direct query/search/count/export/command authorization or retry/idempotency acceptance. Overall migration acceptance and Blazor retirement remain `NOT_READY`. Production rollback/canary, assistive-technology and wider-locale review, live Microsoft gates, and separate owner acceptance remain open.


## Reviewed maintenance recovery increment

The Angular roll-forward and restatement workbenches now use named Application preview, execute and receipt lookup commands. Exact request hashes bind the actor, session epoch, client, action, source fingerprint and normalized input. Explicit assent is required after preview. A retained request reference contains identity and hash only; it does not store assent or editable financial inputs. Receipt recovery returns the original snapshot, and missing lookup results retain the request identity rather than authorizing an unrelated resend.

`PeriodMaintenanceWorkspace` composes `ClientAccountingService` transaction owners. Publication locks the firm, client, actor and affected accounting records, rechecks authorization and writes the effect and append-only receipt atomically. Legacy callers retain transaction ownership. `AccountingPeriodRecordQuery` reauthorizes after composing handoff details. The additive `PeriodMaintenanceReviewedReceipts` migration refuses downgrade when retained receipts exist; it is not a live deployment approval.

Roll-forward and restatement source actions have pinned verification; other rows remain partial. Batched verification results and the tested source belong in `status.json`. Complete role/action/error coverage, the wider accounting workspace matrix and the global retirement criteria remain open. No Blazor file is removed by this increment.

## Scope-bound maintenance drafts

Roll-forward and restatement editors now erase editable financial fields when the client changes, navigation or session invalidation occurs, or a context read fails. An ordinary successful same-client refresh preserves entered evidence. Pending request identity remains in the existing bounded tab checkpoint, independently of draft cleanup. Populated-draft and same-client refresh assertions passed in the combined page-spec cohort; source attribution is in `status.json`. Current browser/source-action reconciliation for these two source rows is complete; broader retirement gates remain open.

### Pending queue and period verification

`eafcf3c5eb2542eb7ea6297cf90b16782d4b0fbb` resets the selected queue's page when
authorized results are replaced, preserving the selected page size and original
counts and links. Ordinary page changes do not trigger this reset. The new specs
cover mappings, journals and differences with real HTTP resources.

The following period-evidence commit adds exact persisted task/PBC owner, due
date and state assertions, exact package/mapping/PBC links, a read-only refresh
check and transient-refresh clearing/retry coverage. These additions have not
yet run in the integrated snapshot; neither source row is promoted here.
