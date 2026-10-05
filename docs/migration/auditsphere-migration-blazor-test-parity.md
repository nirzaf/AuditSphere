# AuditSphere — Blazor / Angular Test Parity Register

> **Status: PARTIAL.** Test names that look similar do not prove replacement coverage. Each candidate mapping must be checked for the host it starts, the user identity/scope it uses, the action/outcome asserted, and the strength of the replacement assertion.

Exact run counts, commits, logs, and external-gate evidence belong in [`docs/execution/status.json`](../execution/status.json). This file records what the current tests prove and the remaining Blazor coupling.

## Current focused evidence

| Evidence | What it proves | What it does not prove | Result |
|---|---|---|---|
| `AngularRoutingContractTests` | API deep-link declarations match Angular route declarations; runtime Angular links avoid legacy workbench paths; non-server-owned legacy workspace paths have a native owner | Screen actions, form parity, backend authorization for each capability, error/recovery parity | Route ownership only |
| `AngularResourcePlanningJourneyTests` | Resource planning page is exercised through the API host in canonical and `/ui` modes; a reviewed allocation rejects a stale concurrent change and recovery reloads persisted state | Full staffing, certification, availability, budget, role/scope and all failure-state parity | Focused slice only |
| `AngularClientScopeAuditDetailJourneyTests` | API-host Angular workpaper, finding and population routes are checked against a sibling client and same-client sibling engagement; guessed IDs clear data; authorized content returns in the same document. The seeded views include a frozen submission, management response and reviewed population sample. Six viewport widths and keyboard focus are checked across the detail routes. | Other audit actions, every revoked-user reload/refresh case, and full audit/release workflow parity | Three removed `AuditAndReleaseJourneyTests` methods mapped; suite remains open. |
| `AngularReleaseParityJourneyTests` | API-host Angular release detail verifies exact engagement access, sibling-scope and guessed-ID denial, expired protection blocking, no-release persistence, same-document stale-state clearing and recovery, six viewport widths, keyboard focus, and clearing after revoked-grant reload. | Other release/signature/records behaviors and production provider acceptance | Two removed release methods mapped; remaining suite parity stays open. |
| `AngularAssessmentDecisionRouteJourneyTests`, `AngularClientScopeEngagementParityJourneyTests`, plus `AcceptanceChecklistTests.Receipts` | Exact legacy assessment-decision deep link in canonical and `/ui` modes; scoped Partner access and unrelated-client Manager denial with no profile disclosure; Partner preview/assent; decision-specific authority revocation; lost-response reload/reconciliation and one-dispatch receipt. | Other acceptance sources, other actions, and whole-family parity | `PARITY_VERIFIED` for `AssessmentDecision.razor` only; the remaining acceptance family stays open. Detailed results are in `status.json`. |
| Former `TenantAdministrationJourneyTests` cases M365-ADMIN-E2E-01..05 | `AngularTenantConnectionJourneyTests` covers consent identity, four independently verified capabilities, responsive tenant/dashboard views, exact directory binding, reviewed client-scoped RoleGrant assignment and revocation evidence. `AngularTenantOperationsJourneyTests` covers one-time-password handling, unknown-outcome reconciliation, client-scoped guest invitation and guest portal restriction, plus reviewed managed-group add/remove and a NOT_GRANTED blocker. `AngularAdministrationJourneyTests` retains local role/scope review and session invalidation. | This replaces those five stale Blazor-selector journeys only; the two remaining removed suites and the source-action inventory still need their own assertion crosswalks. | Focused API/Angular replacement set; exact current run is recorded in `status.json`. |
| Removed `M365SetupJourneyTests` bootstrap/draft methods | `AngularInstallationBootstrapJourneyTests` covers exact deployment-approved identity binding, explicit proof review, fresh session, one-time setup closure, local grant evidence, wrong-tenant/object refusal and no workspace session before binding. `AngularTenantSetupMetadataJourneyTests` covers saved setup display for the exact authenticated tenant, no installation-proof prompt, metadata/verification separation and reload. `AngularTenantSetupEditJourneyTests` covers reviewed metadata revisions and receipt recovery without repeat mutation. `AngularSharePointJourneyTests` covers exact selected site, site ID, library, root and access profile persistence, reviewed immutable template approval, and continued blocking of activation after the resource draft invalidates selected-site verification. `AngularTenantConnectionJourneyTests` and `AngularAdministrationJourneyTests` cover current consent/status separation and persisted setup progress. | The replacement intentionally removes user-editable tenant identity: the authenticated tenant must match the deployment-approved identity. The old numeric 1/7-to-2/7 checklist is replaced by persisted capability/setup progress. The setup workspace opens with saved state and no bootstrap-proof prompt after the approved administrator is bound. These are explicit flow changes; no Microsoft password is collected and no synthetic setup state is presented as live verification. | Both removed methods have passing assertion-level API/Angular maps; combined focused Release cohort passed 9/9 with zero failures or skips. Details follow and exact SHA/build/model checks are in `status.json`. |
| `AcceptanceJourneyTests.PartnerCompletesEvidenceAndSpecialistReviewBeforeAcceptingClient` | API-only Angular route: blocks an evidence-required answer without evidence; records reviewed answers; requires specialist clearance for an adverse answer; confirms HOLD then evidence-backed clearance; records a Partner acceptance; starts a fresh continuance and verifies its question bank and generation; checks persisted immutable receipts and no page errors. | Full acceptance-source inventory, other roles/engagement-specific paths, reload/revocation behavior across every action, and the removed-suite crosswalk | Focused replacement journey verified; no source-family parity promotion. Result is in `status.json`. |
| Removed `InvoiceScopeJourneyTests` method | `AngularInvoiceScopeJourneyTests.FinanceManagerScopedToAnotherClientCannotViewInvoiceOrFallbackBalance` starts API/Angular hosts over isolated PostgreSQL data with two client-scoped FinanceManagers and real invoice IDs. Each can view their own invoice. Same-document Angular route changes to the sibling invoice show the generic unavailable message, clear both clients' line and balance details, preserve the document token, and reload the first invoice on return. A second client context is denied the owner's invoice; no invoice data leaks and neither page raises a browser error. | Production or mid-session identity revocation behavior is outside this method and remains part of the wider open authorization audit. | The single method has a passing assertion-level replacement map; this does not close other billing or source/action coverage. Exact result is in `status.json`. |
| Removed `PracticeBillingLedgerJourneyTests` (4 methods) | `AngularPracticeBillingLedgerParityTests` covers Angular time entry, independent time approval, time-backed invoice source uniqueness, balance/credit/receipt integrity, explicit manual ledger posting, period close, invoice grant revocation, proposal detail scope, stale route clearing, and revoked-session clearing. `AngularClientConversionJourneyTests` covers reviewed conversion without granting portal access or creating an engagement. `AngularBillingWorkspaceJourneyTests` verifies receipt allocation, credit-note recovery, history paging and no duplicate command. `AngularInvoiceScopeJourneyTests` verifies in-place invoice route isolation; `FirmOperationsJourneyTests` covers the expense-to-trial-balance path. | Provider actions that the former E2E asserted through Application services remain server-side service assertions; native Angular form and route actions are exercised through the API host. This closes the four removed E2E methods only and does not promote the Razor source/action inventory. | Passing API-host PostgreSQL/Playwright crosswalk; exact cohort result is recorded in `status.json`. |
| PBC staff and client portal journeys | `AngularPbcStaffJourneyTests` creates/sends a scoped request, queues a follow-up email, completes a bound transfer through the Test simulation worker, checks the refreshed `COMPLETED`/`RECEIVED` UI, downloads exact bytes through the browser and API, and checks safe headers. `AngularClientPortalJourneyTests` exercises first sign-in, delegation/revocation, reply, browser file drop, displayed SHA-256 and persisted chunk state. `AngularClientPortalUploadRecoveryJourneyTests` verifies in-flight multi-chunk progress, lost-ack recovery, capability rotation and exact bytes. `AngularPbcUncertainOutcomeJourneyTests` verifies the inbox's uncertain-provider state and reconciliation after the provider accepted bytes but its response was lost. | Live selected-site worker delivery through the Angular route and the full authorization/expiry/stale/error matrix. The one-method removed `PbcUploadJourneyTests` map is recorded below. | Focused local replacement evidence; both PBC source rows remain partial. Details are in the PBC source review and `status.json`. |
| API-only Angular E2E fixture mode | `OwnedHost` starts the API process through API-named methods; the Web rollback project remains in the solution but API.Tests/E2E.Tests do not reference it | Replacement coverage for legacy assertions deleted by `8ea3ef01`; a passing whole-solution regression does not establish their assertion-by-assertion replacement | Host conversion and the recorded full regression verified locally; broader behavior mapping remains open |
| API.Tests standalone host | The API test project references `AuditSphereOps.Api` directly; its complete PostgreSQL-backed suite runs through `StandaloneApiApplicationFactory` with no Web project reference | Removal of Web from E2E.Tests/solution, legacy smoke replacement, or behavior parity for every Razor action | Full API suite result recorded in `status.json` |
| `AngularLegacyRouteInventoryContractTests` | Every non-server-owned route in the committed Blazor discovery inventory has a matching declared API/Angular route owner; the separate Angular routing contract scans the restored Web source | Feature/action parity or behavior-level user journeys | Focused route-ownership check only |
| Angular CI component/unit suite | Current Angular component and contract tests pass | Blazor-to-Angular equivalence for every Razor artifact | Component coverage only |
| Release solution build and EF model check | Current projects compile and the EF model has no pending migration | Whole-solution behavioral regression or UI parity | Build/model evidence only |
| Built-in browser visit to `/app/practice/resources` on the API host | The canonical Angular resource screen renders with an authenticated Development session and shows the live resource grid/forms | Production behavior, every form command, accessibility with a human screen reader, or a comparison against the Blazor screen | Read-only visual inspection only |
| Current complete whole-solution suite in `status.json` | Current Release snapshot passed the PostgreSQL-backed API, Domain, and E2E projects with zero failures or skips | Source-action parity, removed-suite assertion replacement, production behavior, and external/human acceptance | Current local regression only |

Current exact results and attribution are recorded under `angularBlazorCurrentRetirementGate` in `status.json`.

## Removed route-render and responsive-shell assertion crosswalk

At the current crosswalk checkpoint, all three methods in the two suites below
have API-host Angular replacements. Exact commands, durations and the source
commit are in `status.json`.

| Legacy assertion | Replacement evidence | Disposition |
|---|---|---|
| Each parameterless route renders its expected heading without an unhandled browser error. | `AngularRouteAndShellMigrationSweepTests.ParameterlessRoutesRenderAndPreserveRoleSpecificShells` visits all 26 inventoried paths, checks one visible `main h1`, and collects browser page errors. | Covered for the listed parameterless paths. Detail routes are seeded and checked separately. |
| Staff and client views keep the correct navigation shell; a non-administrator cannot read the project-progress tracker. | The route journey verifies one staff workspace navigation, the client-only portal link and no Administration disclosure. A staff identity without firm-wide Administrator receives the authorization denial with no task bar. | Covered for these identities and the tracker route; this does not replace the wider role/scope matrix. |
| The project-progress view reports real task-card state, and filtering changes the result list without changing the overall distribution. | The route journey checks the overall bar, every published module and audit-phase bar, the audit workflow and shared-foundation bars, and confirms unmapped modules are explained without measured progress. It matches the Completed subset count to the accessible completed count, verifies the published total is stable, and restores the All count. | Covered for the current tracker contract; the old Blazor numeric/color-specific layout is not asserted. |
| The accounting queue has visible keyboard focus; staff and client views fit the responsive matrix. | The route journey tabs focus to the accounting Adjustments link and asserts a solid visible outline. The route journey checks client portal overflow at 320, 390, 760, 1024, 1440 and 1920 pixels. | Covered in the API-host browser journey. |
| Parameterless staff and seeded client, engagement, audit and accounting detail routes reflow, show no more than one exact active link and identify the owning section. | `AngularRouteAndShellMigrationSweepTests.StaffAndDetailRoutesReflowAcrossTheSixViewportMatrix` checks 24 parameterless staff paths plus 10 seeded detail paths at all six widths, asserts no document overflow, at most one exact active link, and the expected current group for each path. Its long legal client name exercises content wrapping. | Covered. The breadcrumb overflow found at 320px was fixed by allowing breadcrumb items to wrap. |
| A disconnected Blazor circuit reports that a pending action is unconfirmed and does not claim success. | `AngularPendingOutcomeReconciliationTests.LostCommandResponse_SurfacesGlobally_DeepLinksAndClearsAfterAcknowledgment` lets the API accept a contact command, drops its response, then checks the persisted outcome across full navigation, the owning-workspace deep link, manual receipt verification, acknowledgment, and no automatic retry. | Covered by Angular's durable command-outcome recovery. Angular has no SignalR circuit, so the transport-specific reconnect overlay is intentionally replaced by a persisted unknown-outcome banner and manual reconciliation. |

These maps close the two removed `ResponsiveShellSweepTests` methods and the
single removed `RouteRenderSmokeTests` method. Combined with the PBC,
invoice-scope, M365 setup, and PracticeBillingLedger maps, 11 of 82 removed
methods are covered; three suites containing 71 methods remain open. This does
not promote any additional Razor source/action row to full parity or close
production, human, or external acceptance gates.

## Removed `PracticeBillingLedgerJourneyTests` method crosswalk

The removed suite contained four methods. Their current replacement paths are
listed below; the locally executed cohort result and commit attribution are in
`docs/execution/status.json`.

| Legacy method | Replacement evidence | Disposition |
|---|---|---|
| `ProspectTimeBillingAndManualFirmCloseKeepTheirBoundaries` (`PROP-E2E-11`) | `AngularPracticeBillingLedgerParityTests.AngularTimeApprovalBillingAndFirmCloseKeepTheirBoundaries` records and submits time in Angular, approves it as a different Partner, rejects duplicate allocation of the same approved time source, checks invoice balance/credit/receipt state and absence of implicit billing journals, posts three manual journals idempotently, and closes the period through the Angular firm-ledger route. `AngularBillingWorkspaceJourneyTests` covers browser-driven receipt allocation and credit-note recovery. `AngularClientConversionJourneyTests` verifies that reviewed conversion creates a prospect with a pending acceptance decision and no engagement or portal access. `FirmOperationsJourneyTests` covers the adjacent expense-to-trial-balance journey. | Covered by the combined API-host tests. Commercial prospect creation and backend accounting transitions are exercised through their current Application contracts; the Angular time and period-close actions are driven in the browser. |
| `InvoiceDetailClearsPriorInvoiceWhenRouteChangesInPlace` (`AS-PAR-002-INVOICE-STALE-ROUTE-01`) | `AngularInvoiceScopeJourneyTests.FinanceManagerScopedToAnotherClientCannotViewInvoiceOrFallbackBalance` changes invoice IDs inside the same Angular document, verifies no prior/sibling invoice number, line or balance remains, preserves a document token, and reloads the authorized invoice on return. | Covered for this route and client-scoped role pair. |
| `InvoiceRefreshClearsAfterGrantRevocation` (`AS-PAR-002-INVOICE-REFRESH-REVOKED-01`) | `AngularPracticeBillingLedgerParityTests.OpenInvoiceContentClearsWhenItsGrantIsRevoked` opens the scoped invoice, revokes its grant, and asserts the live Angular session clears the invoice number and line detail without requiring a user refresh. | Covered with immediate session invalidation. |
| `ProposalDetailShowsPersistedFirmWideProposalAndDeniesScopedIdentity` (`AS-PAR-009-PROPOSAL-READ-01`) | `AngularPracticeBillingLedgerParityTests.ProposalDetailRequiresFirmWideGrantAndClearsStaleOrRevokedContent` checks persisted latest-revision details for a firm-wide RelationshipManager, a generic unavailable alert with no stale content on an unknown ID, immediate content clearing after grant revocation, and denial of the same proposal to a client-scoped identity. | Covered for the current proposal detail route and tested roles. |

## Removed `InvoiceScopeJourneyTests` method crosswalk

The removed suite contained one method, `FinanceManagerScopedToAnotherClientCannotViewInvoiceOrFallbackBalance` (`AS-PAR-002-INV-01`). Its legacy product assertions map to the API-host Angular journey below:

| Legacy assertion | Replacement evidence | Disposition |
|---|---|---|
| A finance manager scoped to client A sees an invoice owned by A, including its private line and amount | `AngularInvoiceScopeJourneyTests` seeds a client-A `FinanceManager`, a real invoice with a distinctive private line and QAR 247.00, then checks the Angular invoice heading, line item and balance. | Covered. |
| Changing the current invoice ID to a real client-B invoice must deny access and remove the previous page's invoice content without a full document navigation | The same browser document changes routes using `history.pushState` and `popstate`. It asserts the generic unavailable message, no client-A/client-B invoice numbers, lines or amounts, and an unchanged document token. Returning to A reloads the authorized invoice and details. | Covered. Blazor prerender and circuit-negotiation checks are host-specific; Angular API route readiness and same-document behavior are asserted directly. |
| A finance manager scoped only to client B cannot read client A's invoice or fallback balance | A separate authenticated API/Angular context first verifies positive access to B's invoice, then requests A's real invoice ID and asserts the generic unavailable state and absence of A/B invoice details and balances. | Covered. |
| No browser page errors occur during the denied route changes | The journey captures Playwright `PageError` events for both contexts and asserts both collections are empty. | Covered. |
| Authorization is applied to the full invoice detail, including balance and private lines | The API route composes `BillingInvoiceWorkspaceQuery.GetAsync` and `BillingService.GetInvoiceDetailAsync`; the service resolves the invoice within the actor's firm and applies the billing role against the invoice's client before returning detail. The browser journey asserts the data is absent on denial. | Covered for this detail route and scoped role pair. Other billing routes and source actions remain open. |

At the time of this slice, this closed the assertion map for the single method in `InvoiceScopeJourneyTests`.

## Removed `M365SetupJourneyTests` method crosswalk

The removed suite contained two methods: `BoundAdministrator_OpensExistingDraftWithOneClick` (`PROP-E2E-05-ADMIN`) and `SetupDraft_SavesAndResumesWithoutClaimingMicrosoftVerification` (`PROP-E2E-05`). The API-host Angular replacement cohort passed 9/9 against isolated PostgreSQL fixtures at the source commit recorded in `status.json`.

| Legacy assertion | Replacement evidence | Disposition |
|---|---|---|
| An already bound firm Administrator opens Microsoft setup without entering the initial installation proof again. | `AngularTenantSetupMetadataJourneyTests.SavedSetupIsDisplayedSeparatelyFromConsentAndLiveVerification` enters the protected tenant-connection route with the existing Administrator, asserts there is no installation-proof field, displays the exact configured tenant ID and saved label, and reloads the persisted setup. `AngularAdministrationJourneyTests` also exercises the Administrator-only overview and denies the Staff identity. | Covered. The new route presents the saved setup workspace directly instead of requiring a separate one-click resume action. |
| The tenant recorded for setup is the signed-in tenant and the friendly name does not replace the immutable tenant identity. | The metadata journey aligns the synthetic authenticated `tid` to the configured tenant, checks the exact tenant ID after reload, and asserts that the label is informational. The bootstrap journey rejects a wrong tenant and wrong object ID. | Covered with a stronger immutable-identity boundary; the browser no longer edits `tid`. |
| Setup progress and the connection checklist reflect actual saved/verified state; saving a working-site URL does not claim selected-site verification or activation. | `AngularTenantConnectionJourneyTests` checks persisted tenant/consent/directory progress. `AngularSharePointJourneyTests` saves the exact selected-resource fields, verifies they persist, and confirms the changed draft invalidates selected-site verification while verification/activation remain blocked. | Covered. The old fixed 1/7-to-2/7 counter is replaced with progress derived from persisted capability/setup state. |
| An administrator can save the local setup draft, reload, and recover it without implying Microsoft consent, Graph permissions, or SharePoint content creation. | `AngularTenantSetupEditJourneyTests` reviews and saves a new metadata revision, verifies the immutable administration event and persisted revision, reloads, recovers the committed receipt, and confirms no second mutation. The metadata journey labels mail/records as draft states, not verification results. The selected-site journey confirms no workspace activation is created. | Covered for reviewed setup metadata and selected-resource/template workflow; live Microsoft acceptance remains separate. |
| The selected site and template details can be configured/reviewed without widening access, and resource changes invalidate earlier verification. | `AngularSharePointJourneyTests.ExactResourceDraftAndTemplateApproval_RemainSeparateFromLiveVerification` reads back the exact site URL, site ID, drive ID, root folder ID, `APP_MEDIATED` profile, revision and template approval from PostgreSQL. It checks that no workspace activation exists and the selected-site capability is `BLOCKED_EXTERNAL` after the reviewed resource change. | Covered by the current standalone selected-resource workspace. |
| The flow remains responsive, keyboard usable and free of browser page errors. | Tenant metadata, tenant connection and selected SharePoint journeys assert no horizontal overflow at 320, 390, 760, 1024, 1440 and 1920 pixels; setup/SharePoint journeys assert keyboard focus, and the focused journeys collect page errors. | Covered in the local API-host Angular journeys. |

At the time of this slice, this closed the two removed method assertion maps. The tenant-identity field and fixed checklist behavior are intentionally replaced by the authenticated deployment-approved identity and persisted progress required by the current architecture. It does not promote the 13 Administration source/action artifacts to full parity, nor establish live Microsoft, production or human accessibility acceptance.

## Removed `PbcUploadJourneyTests` method crosswalk

The removed suite contained one method, `ClientUpload_ReconcilesUncertainProviderResult_AndStaffDownloadsExactBytes` (`PROP-E2E-06`). Its product assertions now map to current API-host Angular/API tests:

| Legacy assertion | Replacement evidence | Disposition |
|---|---|---|
| Client opens the scoped request, selects/drops a PDF, sees its browser-computed SHA-256, then uploads it | `AngularClientPortalJourneyTests.ClientPortal_FirstSignIn_Delegation_Conversation_Upload_AndRevocation` dispatches a browser `DataTransfer` drop and compares the displayed fingerprint to the fixture bytes' SHA-256. | Covered; Blazor circuit connection is an obsolete host detail under API/Angular. |
| Upload reports byte progress and persists the complete chunk receipt/hash | `AngularClientPortalJourneyTests` checks the `CHUNKING` state, byte count, digest and persisted chunk. `AngularClientPortalUploadRecoveryJourneyTests.LostChunkAcknowledgement_ResumesExactFileFromPersistedChunkReceipt` holds the resumed second chunk and verifies in-flight byte progress, then covers offsets and exact reconstruction. | Covered. |
| Staff sees scoped request/upload counts, can complete the staged transfer, and the inbox fits at 320, 390, 760, 1024, 1440 and 1920 pixels, including the recipient control | `AngularPbcStaffJourneyTests.StaffInbox_CreatesRequest_RequestsMoreFiles_AndQueuesVerifiedTransfer` checks scoped counts and completes the transfer. `AngularPbcUncertainOutcomeJourneyTests.StaffInbox_ShowsUncertainProviderOutcome_ThenReconcilesExactUpload` checks all six widths, document overflow and recipient bounds. | Covered. |
| Provider stores the exact file but the worker loses its response; the operation is `RESULT_UNCERTAIN`, the upload stays staged, and the request remains partially received | The uncertain-outcome journey uses the scripted provider against the isolated PostgreSQL fixture and asserts all three persisted states. | Covered. |
| Staff sees staged plus uncertain in the Angular inbox; reconciliation finds the accepted remote file without creating a duplicate and updates the UI | The same journey reloads the inbox to assert `staged` / `result uncertain`, then reconciles, checks `RECEIVED` / `COMPLETED`, exact digest and one simulated provider file. | Covered with a normal-CI provider fake; live SharePoint behavior remains external. |
| Staff follows the Download link and receives the exact bytes with safe headers | The staff journey performs an actual browser download and compares bytes; its authenticated API download assertion checks exact bytes, `no-store` and `nosniff`. | Covered. |
| The client cannot download that same staged/received upload | `PbcHttpTests.DownloadIsScopedAndReturnsExactBytesWithSafeHeaders` now requests the exact upload as its assigned ClientUser and asserts `403`; sibling-client denial remains separately checked. | Covered at the protected API boundary. |

At the time of this slice, this closed the assertion map for the single method in `PbcUploadJourneyTests`; it does not promote either PBC Razor artifact to full source/action parity. Live selected-site delivery through Angular and the wider PBC authorization/state/failure matrix remain open.

## Removed `ClientScopeJourneyTests` audit-detail method crosswalk

`AngularClientScopeAuditDetailJourneyTests.AuditDetailRoutesClearSiblingAndRevokedContentInTheSameDocument`
now maps seven detail, archive, review and revocation methods. It uses scoped
Application fixtures and browser routes on the Angular host; sibling and
random IDs share the same generic unavailable state, and same-document
navigation preserves the document while clearing protected content.

| Legacy method | Replacement evidence | Disposition |
|---|---|---|
| `FindingDetailsRequireCurrentClientAndEngagementScope` | `AngularClientScopeAuditDetailJourneyTests.AuditDetailRoutesClearSiblingAndRevokedContentInTheSameDocument` verifies assigned staff can read the finding impact and management response. A same-document request for the sibling client's record and a random ID render indistinguishable states; private values, amount, and page errors are absent. | Covered. |
| `AuditPopulationAndLinkedReceiptsRequireCurrentEngagementScope` | The same journey verifies the authorized population purpose, extraction parameters, and receipt token. The source filename remains hidden even on the authorized route. Sibling and guessed IDs expose no source marker, filename, or monetary total and render the same unavailable state. | Covered. |
| `WorkpaperAndSubmissionHistoryRequireCurrentEngagementScope` | The journey verifies the assigned workpaper title, objective, procedure, and frozen submission conclusion. A sibling-client ID is denied. After the exact Staff grant is revoked, a draft save through `AuditPlanningService` is refused with no `WorkpaperDraft` persisted, and both open Angular pages clear all seeded workpaper details. | Covered for the scoped detail, submission-history, stale-content, and no-write assertions. |
| `ReviewPointReadAndDispositionAreEngagementScoped` | The journey reads the assigned review comment, clears and reopens the point through the Angular controls, and checks both persisted states. Sibling and guessed IDs expose no review content. | Covered. |
| `RecordsArchiveClearsPriorManifestOnUnauthorizedRouteChange` | The same journey checks the authorized archive manifest summary, one-entry count, profile, entry and digest; it verifies six viewport widths and keyboard focus, then navigates in the same document to sibling and random IDs and confirms the prior archive data clears before returning to the authorized archive. | Covered for archive summary, responsive layout, focus, stale-content clearing and recovery. |
| `ReviewPointClearsPriorPointOnSameDocumentIdChange` | The journey checks that an authorized significant review point shows its open completion-blocker state, then covers six viewport widths and keyboard focus. Sibling and random point IDs clear both comments, review context and the action; the authorized route restores its content without replacing the document. | Covered for blocker state, responsive layout, focus, stale-content clearing and recovery. |
| `ReviewPointClearsProtectedStateWhenCurrentGrantIsRevoked` | After grant revocation, the review disposition command is refused and the point remains open. The two already-open Angular pages clear the comment, blocking state, and action controls; the test records no browser page errors. | Covered. |

The latest focused PostgreSQL-backed case passed 1/1 in 37 seconds at test
commit `fc87cc66`. The same-client sibling-engagement, responsive and keyboard
checks now apply to workpaper, finding and population details as well as archive
and review. The test retains the review disposition and two-open-page
revocation assertions. The Release solution build and EF no-drift check were
recorded at the earlier source slice; this update is test-only. The shared
worktree had an unrelated unstaged edit to `ConsolidationOverviewQuery.cs`; it
was not included in the commit. The five ClientScope methods listed above
remain mapped.

## Removed `ClientScopeJourneyTests` client-profile method crosswalk

At test commit `fbca7aea`, the API-host Angular client-profile journey adds
same-document isolation for an actual sibling client in both `/ui` and
canonical route modes. Client-A Staff and client-B Manager identities each
see their own client profile; the client-A fixture also exposes its seeded
registration, contact, and engagement markers. Changing the client ID in
place clears protected details; a random ID produces the same denial text.
Returning to the authorized client restores its profile without replacing the
document.

| Legacy method | Replacement evidence | Disposition |
|---|---|---|
| `ClientProfileRequiresCoveringClientGrant` | `AngularClientProfileJourneyTests.ScopedMetadataIndependentPagesReloadMobileAndRevocation` proves the scoped staff identity can read its assigned client, while its real sibling-client route and a guessed ID are both unavailable. `SiblingClientIsolationJourneyTests.SiblingClientDataNeverChangesWhatAClientScopedUserSees` independently denies sibling client profile details under both client and engagement grant fixtures. | Covered by the combined scoped-profile and sibling-isolation assertions. |
| `ClientProfileClearsPriorClientOnUnauthorizedSameDocumentRouteChange` | The same client-profile journey changes from its authorized client to a real sibling client without reloading, checks private profile markers disappear, compares the sibling response with a random ID, and confirms authorized content returns while a document token remains. | Covered. |

The focused PostgreSQL-backed Playwright profile cohort passed 2/2 in 48
seconds. The Release solution build passed with zero warnings/errors, EF found
no pending model changes, and the built-in browser rendered the generic denial
for an unknown client ID. The latest complete solution regression remains
985/985 at `eb94ae50`; it was not rerun for this focused slice. An unrelated
unstaged edit to `ConsolidationOverviewQuery.cs` was excluded from the test
commit.

## Removed `ClientScopeJourneyTests` advanced consolidation crosswalk

The API-host Angular/PostgreSQL journeys cover the legacy forged-group-grant
and same-document stale-scope cases.

| Legacy method | Replacement evidence | Disposition |
|---|---|---|
| `AdvancedConsolidationReadRejectsClientIdentityWithErroneousGroupGrant` | `AngularAdvancedConsolidationAuthorizationJourneyTests.ClientIdentityWithErroneousGroupGrantCannotReadAdvancedConsolidation` gives a Client identity an erroneous `AccountingPreparer` group grant. The staff route guard directs it to the client portal; a direct request to the scope API is denied with a generic response that contains neither the private group name nor scope ID. A Staff identity with the exact group grant can read the scope and method. | Covered for the erroneous client-group grant, API denial, disclosure checks and authorized group reader. |
| `AdvancedConsolidationClearsPriorGroupWhenRouteChangesInPlace` | `AngularAdvancedConsolidationAuthorizationJourneyTests.AdvancedConsolidationClearsPriorGroupWhenRouteChangesInPlace` changes an authorized scope ID to an unauthorized sibling in the same Angular document, verifies protected group details clear, then restores the authorized scope. It checks the browser draft after reload, document continuity during route changes, bounded table scrolling and visible keyboard focus across the viewport matrix. | Covered for route denial, stale-content clearing, authorized recovery, browser draft persistence and the legacy viewport/focus assertions. |

The focused PostgreSQL-backed Playwright cohort passed with no failures or
skips. Angular build, Release solution build and EF model-drift checks passed;
the built-in browser showed the Development identity's safe no-group-access
state and a visible keyboard focus ring. The latest complete solution run and
the aggregate open-method count remain attributed in `status.json`.

## Removed `ClientScopeJourneyTests` firm-wide revocation crosswalk

The API-host Angular/PostgreSQL journey covers the legacy firm-wide
administration and workspace refresh cases. It also fixed the stale
invitation-copy control by projecting only invitations whose linked access
grant remains active.

| Legacy method | Replacement evidence | Disposition |
|---|---|---|
| `FirmAdministrationRejectsClientIdentityWithErroneousAdminGrant` | `AngularFirmScopeRevocationParityJourneyTests.ErroneousClientAdministratorGrantIsDeniedAndRevocationClearsNativeWorkspaces` gives a Client identity an erroneous firm-wide Administrator grant, verifies the browser remains in the client portal, and confirms the administration overview and Users & Access APIs both return 403 without exposing the identity or administrator workspace. | Covered for the forged client grant, route classification, API denial and disclosure checks. |
| `FirmAdministrationRefreshClearsDirectoryAfterAdminGrantRevocation` | The same journey searches a local identity, confirms a copy invitation action, revokes the invitation's role grant, retries the copy action and verifies no clipboard write occurs, the server returns 403, and the refreshed access projection removes the stale copy control. It then revokes the open administrator's grant and verifies the Users & Access page clears after refresh while its document token remains. | Covered for invitation grant revalidation, stale-control clearing and administrator-session revocation. |
| `OperationsRefreshClearsLedgerAfterAdminGrantRevocation` | The journey reads a private durable operation, confirms `LOCAL_ONLY` mode and the attention count, checks the six viewport widths and visible keyboard focus, then revokes Administrator access and verifies the open Operations page clears after refresh while its document token remains. | Covered for authorized operation details, recovery-state summary, responsiveness, keyboard focus and post-revocation clearing. |
| `PracticeLeadsRefreshClearsCommercialRowsAfterGrantRevocation` | The journey reads a marker-named firm-wide lead, revokes the RelationshipManager grant, refreshes the open route, and verifies the lead is absent while the document token remains. | Covered for current-grant revalidation and stale commercial-row clearing. |
| `FirmFinanceRefreshClearsLedgerAfterGrantRevocation` | The journey reads a marker-named firm account, revokes the FinanceReviewer grant, refreshes the open route, and verifies the account is absent while the document token remains. | Covered for current-grant revalidation and stale finance-row clearing. |

The focused browser journey, Angular unit suite and Release solution build
passed; EF reported no pending model changes. The latest complete solution
regression is attributed to its earlier frozen checkpoint; current-slice
focused results and the checkpoint boundary are recorded in `status.json`.

## Removed `ClientScopeJourneyTests` portfolio, queue and period scope crosswalk

Eight additional removed methods now have API-host Angular assertions using
isolated PostgreSQL fixtures. The table also records the previously verified
firm-wide ledger/leads method, bringing the documented `ClientScopeJourneyTests`
crosswalk to 23 methods. The focused cohort passed 15/15 tests with no failures
or skips. The journeys exercise API denials and rendered scope, not only route
ownership.

| Legacy method | Replacement evidence | Disposition |
|---|---|---|
| `FirmLedgerAndLeadsRequireFirmWideRoleGrants` | `AngularFirmWideScopeJourneyTests.FirmLedgerAndPracticeLeadsRequireFirmWideRoleGrants` seeds firm-wide FinanceReviewer and RelationshipManager roles plus client-scoped FinanceManager/RelationshipManager, Partner and Client identities. Only the firm-wide finance and commercial roles see ledger or lead data; the client identity remains in the client portal. | Covered for firm-wide role requirements and refusal to widen client-scoped or client-classified identities. |
| `ClientIdentityWithErroneousFirmWideStaffGrantCannotViewPortfolio` | `AngularPortfolioJourneyTests.ClientIdentityWithErroneousFirmWideStaffGrantCannotReadPortfolio` gives a Client identity an erroneous firm-wide Staff grant and checks that the scoped portfolio API and Angular workspace do not reveal the staff portfolio. | Covered for the forged role grant, protected read denial and private-data non-disclosure. |
| `PortfolioCsvRechecksGrantAndClearsRowsAfterRevocation` | `AngularPortfolioJourneyTests.ExportRevocationClearsPortfolioAndDoesNotProduceAnotherDownload` revokes the scoped grant after an authorized portfolio read, then verifies the open workspace clears and a subsequent export is refused without producing another download. `Api.download()` also refreshes the session after HTTP 401; its unit test verifies the expired session is cleared. | Covered for export-time authority revalidation, stale-row clearing and failed-export session invalidation. |
| `EngagementScopedTimeQueueHidesSiblingTasksEntriesAndClientPeriods` | `AngularPracticeTimeScopeParityJourneyTests.EngagementScopedTimeQueueHidesSiblingTasksEntriesAndClientPeriods` seeds assigned and sibling engagement tasks/time entries plus a client-level period. An engagement-only Manager sees only the assigned task and narrative. | Covered for engagement isolation across the task queue, time entries and client-period projection. |
| `AssessmentDecisionIdResolvesScopeBeforeExposingAssessmentDetails` | `AngularAssessmentDecisionRouteJourneyTests.ExactDecisionIdRequiresItsClientGrantAndKeepsAssessmentDetailsScoped` exercises canonical and `/ui` routes for an exact assessment ID, an unauthorized identity and a random ID. Exact and guessed IDs return indistinguishable denials; authorized content is checked across six viewport widths and keyboard focus. | Covered for ID-to-client authorization before disclosure, guessed-ID indistinguishability, responsive layout and focus. |
| `ClientScopedPartnerCannotSeeAccountingEvidenceFromAnotherClient` | `AngularAccountingEvidenceQueueJourneyTests.PartnerEvidenceQueueShowsOnlyTheAssignedClient` checks that a client-scoped Partner sees the assigned client's evidence and count only. `ScopedEvidenceCountsRemainIsolatedAndDisappearAfterEpochLoss` checks sibling-marker absence and clears evidence after session-epoch loss. | Covered for client isolation, count scoping and stale-content clearing after session invalidation. |
| `AccountingWorkspaceIgnoresOtherRoleAndMismatchedEngagementGrants` | `AngularAccountingScopeParityJourneyTests.MismatchedAndOtherRoleGrantsDoNotExpandAngularAccountingWorkspace` combines a matching client grant with other-role and mismatched engagement grants, checks search/list/detail API results, and denies the sibling client's period. | Covered for role and engagement mismatch non-widening across Angular and API reads. |
| `AccountingWorkspaceHidesSiblingEngagementTaskSummary` | `AngularAccountingTaskOwnerScopeJourneyTests.AccountingWorkspaceShowsOnlyOwnersInAssignedEngagementAndClearsAfterRevocation` reads the owner/count projection through the Angular page and same-origin API. It shows the assigned engagement's owner, hides the sibling engagement's owner, omits task titles and owner IDs, and clears/refuses the projection after the current grant is revoked. `AccountingTaskOwnerQuery` also verifies the stored engagement/client link and re-resolves grants before returning. | Covered for task-owner scope isolation, minimized projection and current-grant revocation. |
| `AccountingPeriodRequiresClientGrantAndClearsPriorPeriodOnRouteChange` | The same accounting journey navigates from an authorized period to a sibling period and a random period ID in-place. Both denials are identical and the previously visible period markers are cleared. | Covered for exact client-period scope, guessed-ID indistinguishability and stale period clearing. |
| `PeriodMaintenanceQueuesDoNotWidenEngagementGrantToClientPeriodScope` | `PeriodWorkbenchScopeJourneyTests.PeriodMaintenanceRequiresClientOrFirmScopeInAngularRoutes` checks roll-forward and restatement queues for a client-scoped Partner and an engagement-only AccountingPreparer. The client grant sees the period; the engagement-only identity sees neither period marker nor client name. | Covered for client-level period maintenance authorization and refusal to widen an engagement grant. |

The code commit is `beee6480`. Angular production build and unit tests passed;
the initial bundle is 506.60 kB, 6.60 kB over the 500 kB warning budget. The
Release solution build passed with zero warnings/errors, and EF reported no
pending model changes. The built-in browser rendered the authenticated local
Angular accounting route read-only. A clean whole-solution run remains
attributed to its earlier checkpoint, before this code commit. AS-PAR-002 and
Blazor retirement remain partial/`NOT_READY`.

## Removed `ClientScopeJourneyTests` PBC portal and inbox scope crosswalk

`AngularPbcScopeParityJourneyTests` now has four API-host Playwright journeys
that replace eight removed client/staff PBC authorization methods. The tests
use isolated PostgreSQL fixtures and exercise both authorized and denied
identities, exact request IDs, same-document transitions, grant revocation,
and persisted no-write outcomes.

| Legacy method | Replacement evidence | Disposition |
|---|---|---|
| `ClientPortalHidesPbcRequestsOutsideAssignedEngagement` | `ClientPortalHidesUnassignedSiblingRequestsDraftsAndOtherClients` seeds a sent request on an unassigned sibling engagement and verifies it is absent from the Angular portal list and unavailable by its exact ID. | Covered for engagement-level client isolation. |
| `ClientPbcRequestClearsThreadWhenNavigatingToAnotherRecipientsRequest` | The same journey authorizes the client's own request, then changes the route in-place to a sent request assigned to a different recipient in the same engagement. It verifies both private markers disappear, the denied response is shown, and the document token remains. | Covered for recipient-level request isolation and stale thread clearing. |
| `PbcInboxRequiresAllowedRoleGrantForTheTargetEngagement` | `PbcInboxRequiresPbcRoleAndClearsAfterStaffGrantRevocation` gives a Staff-kind identity only a FinanceManager grant on the target engagement. The PBC API/UI show a generic scope denial without the seeded private marker. | Covered for role-specific PBC denial. |
| `RevokedStaffGrantClearsOpenPbcInboxAfterNextCommand` | The same staff journey opens the PBC inbox, types a private request-more-files draft, revokes the exact Staff grant and attempts the command. The session is invalidated; the inbox and draft clear, and no matching communication is persisted. | Covered for current-grant revalidation, session invalidation, stale-content removal and no-write refusal. |
| `RevokedClientGrantClearsOpenRequestAfterNextCommand` | `ClientReplyIsRefusedAndClearedAfterGrantRevocation` opens an assigned request, types a private reply, revokes the ClientUser grant and submits. The portal returns to its signed-out protection state, clears request/reply content, and persists no reply. | Covered for client-side command reauthorization and revoked-session clearing. |
| `ClientCannotSeeOwnUnsentDraftPbcRequest` | The portal scope journey verifies the client's own unsent DRAFT request is absent from the list and its exact route returns the same generic unavailable state as the other unauthorized requests. | Covered for draft invisibility and direct-ID denial. |
| `UnrelatedClientCannotViewSiblingPbcRequestOrItsDescription` | The same journey first reads an unrelated client's own sent request, then navigates in-place to the original client's request ID. It gets the generic unavailable state, no private markers, and retains the document token. | Covered for positive control, cross-client denial and stale-content clearing. |
| `PbcInboxClearsRenderedStateWhenNavigatingToUnauthorizedSiblingEngagement` | `StaffInboxClearsOnUnauthorizedSiblingEngagementNavigation` opens the assigned staff inbox, changes the engagement route within the same Angular document, and verifies the prior and sibling request markers are absent with a scoped denial. Returning to the assigned engagement restores its request without replacing the document. | Covered for sibling-engagement route reauthorization, stale-content clearing and authorized recovery. |

The focused PostgreSQL-backed browser cohort passed 4/4 in 1m11s with zero
failures or skips at test commit `41579171`. The test-only commit does not
change application or EF models. The
latest whole-solution run remains attributed to its earlier checkpoint; the
current crosswalk and full-suite boundaries are tracked in `status.json`.

## Removed `ClientScopeJourneyTests` assessment, engagement and completion scope crosswalk

Five more removed client-scope methods now have API-host Angular assertion
replacements. The focused Release Playwright cohort passed 4/4 with isolated
PostgreSQL fixtures, including both canonical and `/ui` acceptance-decision
routes. The evidence covers exact client/engagement grants, private-data
non-disclosure, same-document route changes, authorized recovery, and
revocation-triggered command refusal/session invalidation.

| Legacy method | Replacement evidence | Disposition |
|---|---|---|
| `AcceptanceDecisionRequiresPartnerGrantForExactClient` | `AngularClientScopeEngagementParityJourneyTests.AcceptanceDecisionRequiresPartnerGrantForExactClient` visits the deep link in canonical and `/ui` modes. A Partner scoped to the exact client sees the assessment profile and review action; a Manager assigned to a different client receives the access denial without the profile or private registration marker. | Covered for the required Partner/client grant, unrelated-client denial and sensitive profile non-disclosure. |
| `EngagementDetailClearsPriorEngagementWhenRouteChangesInPlace` | `AngularClientScopeEngagementParityJourneyTests.EngagementAndAuditPlanClearOnScopeChangeAndRefuseRevokedWrites` opens the assigned engagement, switches in the same document to an unassigned sibling engagement in the same client, checks both IDs and the private hold reason are cleared, and confirms the document token remains. Returning to the assigned engagement restores its ID and hold projection. | Covered for exact engagement scope, stale route clearing, document preservation and authorized recovery. |
| `CompletionChecklistDeniesSiblingEngagementAndKeepsRepresentationPrivate` | `AngularClientScopeEngagementParityJourneyTests.CompletionChecklistDeniesSiblingEngagementAndKeepsRepresentationPrivate` confirms assigned Staff can read the exact written-representation narrative/code. A Manager scoped to another client receives the safe completion-scope denial and sees neither marker. | Covered for authorized completion read and cross-client sensitive-data isolation. |
| `EngagementDetailDeniesUnassignedScopeAndClearsOnRouteChange` | The combined engagement journey verifies assigned client and hold data, then changes the same document to an unrelated-client engagement. Client name, service route and private hold data clear while the document token remains. A separate unrelated-client Manager is denied both direct engagement-detail and audit-plan reads without private markers. | Covered for same-document cross-client reauthorization, protected-state clearing and direct-scope denial. |
| `AuditPlanRequiresExactEngagementGrantAndClearsOnRouteChange` | The combined journey reads a private risk under the exact Staff engagement grant, changes to an unassigned sibling engagement and verifies the risk clears, then restores the authorized route. After the grant is revoked, an Application command using the stale actor fails with `GenerationStale`; the browser session is invalidated and no risk is persisted. | Covered for exact engagement access, route clearing, stale command refusal, session invalidation and no-write outcome. |

The focused cohort passed 4/4 with zero failures or skips at that test commit.
At that checkpoint, the crosswalk stood at 50/82 removed E2E methods, including
all 39 `ClientScopeJourneyTests` methods. The later release-safety mapping is
recorded below. Exact SHA, duration, command and latest complete-suite boundary
are in [`status.json`](../execution/status.json). Overall source/action parity
and Blazor retirement remain partial/`NOT_READY`.

## Removed `AuditAndReleaseJourneyTests` release-method crosswalk

The removed suite's release-candidate route and expired-protection journeys now
map to `AngularReleaseParityJourneyTests` on the API host. Its focused
PostgreSQL-backed Playwright run passed 1/1 at source commit `e82c3d2b`; full
solution regression and external production acceptance were not rerun or
implied.

| Removed legacy method | Angular assertion replacement | Disposition |
|---|---|---|
| `ReleaseClearsPriorCandidateWhenRouteChangesInPlace` | `ExpiredProtectionBlocksIssueAndReleaseDetailsClearOnScopeChangeAndRevocation` opens the exact candidate, checks candidate identity and incomplete preflight, changes the same Angular document to a guessed ID and asserts the digest, profile and checkpoint markers clear while a document token persists, then returns to the authorized candidate. Six viewport widths and keyboard focus are checked. | Covered for the tested candidate detail, route transition, recovery and responsive/focus assertions. |
| `ExpiredProtectionBlocksReleaseAndIsNeverPresentedAsVerified` | The same journey seeds a stale protection attestation, verifies the UI says expired and never verified, makes no external-provider acceptance claim, disables issuance, and confirms the Application rejects the issue attempt without a Release row. A sibling-engagement Staff identity sees no candidate details; after grant revocation and reload, the page no longer shows protected release evidence. | Covered for expired-attestation refusal, scope denial, no-write result and stale-content clearing. |

The removed-method crosswalk now covers 52/82 methods, including all 39
`ClientScopeJourneyTests` methods and these two release methods. Thirty methods
remain: 11 in `AuditAndReleaseJourneyTests` and 19 in
`FinancialArtifactJourneyTests`. Exact run and commit evidence is in
`docs/execution/status.json`. Blazor retirement remains `NOT_READY`.

## Removed `AuditAndReleaseJourneyTests` audit-detail route crosswalk

At test commit `fc87cc66`,
`AngularClientScopeAuditDetailJourneyTests.AuditDetailRoutesClearSiblingAndRevokedContentInTheSameDocument`
maps three legacy detail-route journeys through the API-host Angular UI and
isolated PostgreSQL records.

| Removed legacy method | Angular assertion replacement | Disposition |
|---|---|---|
| `WorkpaperClearsPriorWorkpaperWhenRouteChangesInPlace` | The exact Staff grant reads workpaper objective, procedure, title and frozen submission. In the same document, a sibling-client ID and an unassigned same-client sibling-engagement ID clear all protected workpaper content; a guessed ID matches the generic sibling-client denial. The route is checked at six widths with keyboard focus, and the authorized route restores its data. | Covered for the scoped detail, stale-route denial, recovery and responsive/focus assertions. |
| `FindingClearsPriorFindingWhenRouteChangesInPlace` | The API `GET /api/ui/findings/{id}` displays the finding impact and management response only for the authorized engagement. Sibling-client, sibling-engagement and guessed IDs clear prior content; returning to the authorized ID restores the finding. Six viewport widths and keyboard focus are checked. | Covered for the detail state and tested scope boundaries. Other finding commands remain open. |
| `AuditPopulationClearsPriorPopulationWhenRouteChangesInPlace` | The API `GET /api/ui/audit/populations/{id}` displays the 12,000-row population, QAR control total, extraction parameters, sample rationale and one reviewed item test. Sibling-client and same-client sibling-engagement IDs clear the rows, amount, receipt and rationale; the authorized record returns without a document reload. Six viewport widths and keyboard focus are checked. | Covered for population detail, reviewed sample summary, stale-state clearing and recovery. Other audit sampling mutations remain open. |

This adds three mappings to the two release-method mappings above. The
crosswalk now covers 55/82 removed E2E methods, including all 39
`ClientScopeJourneyTests` methods and five `AuditAndReleaseJourneyTests`
methods. Twenty-seven remain: eight in `AuditAndReleaseJourneyTests` and 19 in
`FinancialArtifactJourneyTests`. Exact run and full-suite boundaries are
recorded in `docs/execution/status.json`; Blazor retirement remains
`NOT_READY`.

## Removed `AuditAndReleaseJourneyTests` engagement-route crosswalk

At test commit `814d6428`, the API-host Angular engagement parity cohort adds
same-document route clearing and recovery checks for completion, fieldwork and
audit planning. The PostgreSQL-backed focused cohort passed 5/5 with no
failures or skips.

| Removed legacy method | Angular assertion replacement | Disposition |
|---|---|---|
| `CompletionClearsPriorEngagementWhenRouteChangesInPlace` | `AngularClientScopeEngagementParityJourneyTests.CompletionAndFieldworkClearPriorEngagementWhenRouteChangesInPlace` loads a synthetic written-representation narrative/code for the assigned engagement, changes to an unassigned sibling engagement in the same document, confirms both markers are absent, then returns and confirms the authorized representation is restored. Six viewport widths and keyboard focus are checked. | Covered for stale completion-content clearing, authorized recovery, responsiveness and focus. |
| `AuditFieldworkClearsPriorEngagementWhenRouteChangesInPlace` | The same journey publishes/adopts the controlled 2026.1 audit program and confirms its 165 procedures. A same-document transition to the unassigned sibling clears the program/version/count; returning restores the persisted program. | Covered for the fieldwork mutation, stale program clearing and recovery. |
| `AuditPlanClearsPriorEngagementWhenRouteChangesInPlace` | `AngularClientScopeEngagementParityJourneyTests.EngagementAndAuditPlanClearOnScopeChangeAndRefuseRevokedWrites` checks the exact risk marker disappears at the unassigned sibling route and returns on the authorized route, in the same document. It also checks six viewport widths and keyboard-visible focus. | Covered for audit-plan route denial, stale-state clearing, recovery, responsiveness and focus. |

This adds three mappings to the prior five audit/release mappings. The
crosswalk now covers 58/82 removed E2E methods, including all 39
`ClientScopeJourneyTests` methods and eight `AuditAndReleaseJourneyTests`
methods. Twenty-four remain: five in `AuditAndReleaseJourneyTests` and 19 in
`FinancialArtifactJourneyTests`. Exact current totals and evidence attribution
are recorded in `docs/execution/status.json`; Blazor retirement remains
`NOT_READY`.

## Removed `AuditAndReleaseJourneyTests` revocation and fieldwork crosswalk

At test commit `cd162be1`, three API-host Angular journeys cover the last five
methods from the removed audit/release suite. The focused PostgreSQL-backed
Release E2E cohort passed 3/3 with no failures or skips.

| Removed legacy method | Angular assertion replacement | Disposition |
|---|---|---|
| `ReloadCurrentTargetClearsWorkpaperAfterGrantRevocation` | `AngularAuditRevocationParityJourneyTests.RefreshAfterGrantRevocationClearsWorkpaperFindingAndAuditPlan` opens the exact scoped workpaper, revokes the staff grant through the normal administration service, refreshes the page and confirms the access-unavailable state contains none of the workpaper's previously visible protected values. | Covered for refreshed workpaper state after local grant revocation. |
| `FindingRefreshClearsAfterGrantRevocation` | The same journey independently loads the scoped finding in a separate browser context, revokes the grant, refreshes, and verifies the prior impact marker is removed. | Covered for refreshed finding state after local grant revocation. |
| `AuditPlanRefreshClearsAfterGrantRevocation` | The same journey independently loads the scoped audit plan, revokes the Partner grant, refreshes, and verifies the previously visible materiality facts are removed. | Covered for refreshed audit-plan state after local grant revocation. |
| `AuditFieldworkRecordsHumanAggregateConclusionBoundToDifferenceSchedule` | `AngularAuditFieldworkParityJourneyTests.AggregateDifferenceConclusionRetainsHumanDecisionAndExactSourceSnapshot` records the practitioner conclusion through Angular and verifies persisted submission status and immutable assessment metadata, including the exact difference ID, description and QAR amount in the source snapshot. | Covered for the human-authored conclusion and its exact persisted source binding. |
| `AuditProgramAndReviewedFieldworkSurviveReconnectAndFreezeWorkpaper` | `AngularAuditFieldworkParityJourneyTests.AdoptedProgramReviewedFieldworkAndFrozenWorkpaperSurviveReload` publishes/adopts the controlled program, verifies its procedure set and filtering, records fieldwork, reconnects, reloads and submits the workpaper; persisted reviewed and frozen states are checked. | Covered for tested program adoption, fieldwork, browser reload and workpaper freeze. |

The removed-method crosswalk now covers 63/82 methods, including all 39
`ClientScopeJourneyTests` methods and all 13 `AuditAndReleaseJourneyTests`
methods. Nineteen methods remain in `FinancialArtifactJourneyTests`. The full
solution was not rerun at this test commit; current-source evidence is the
focused cohort only. Exact command, duration and full-suite boundary are in
`docs/execution/status.json`. Blazor retirement remains `NOT_READY`.

## Migration test gaps that keep retirement unaccepted

- The committed API.Tests and E2E.Tests projects reference `AuditSphereOps.Api` directly and do not reference `AuditSphereOps.Web`; the Web project remains in the solution as a rollback/reference host. This establishes a project boundary only.
- `AngularLegacyRouteInventoryContractTests` checks the committed discovery snapshot for route ownership. `AngularRoutingContractTests` scans the restored Web source and checks link/route ownership; neither is feature parity.
- Commit `8ea3ef01` removed nine legacy E2E suites: `AuditAndReleaseJourneyTests`, `ClientScopeJourneyTests`, `FinancialArtifactJourneyTests`, `InvoiceScopeJourneyTests`, `M365SetupJourneyTests`, `PbcUploadJourneyTests`, `PracticeBillingLedgerJourneyTests`, `ResponsiveShellSweepTests`, and `RouteRenderSmokeTests`. The PBC upload and invoice-scope methods, both M365 setup methods, both responsive-shell methods, the route-render method, all four PracticeBillingLedger methods, all 39 `ClientScopeJourneyTests` methods, and all 13 `AuditAndReleaseJourneyTests` methods now have assertion-level replacement maps. Nineteen `FinancialArtifactJourneyTests` methods remain open; exact totals and evidence attribution belong in `status.json`.
- Focused API-only role and browser journeys pass for selected sources; the API-only E2E host conversion is committed, and the recorded full regression passed locally at its attributed commit. The removed-suite replacement crosswalk remains open until the remaining assertions are mapped. Its state is tracked in `docs/execution/status.json`.
- Existing test crosswalk rows are candidate relationships until the host and assertions have been checked. A shared test file or reused fixture is not itself a replacement test. Aggregate mapped and remaining method counts belong in `status.json`.

## Acceptance rule

For each migrated behavior, link the source action to its API contract/Application authority and to a test that actually uses the API/Angular host. Confirm positive and negative authorization, exact scope isolation, validation, audit result, concurrency/stale behavior, retry/idempotency, upload/download integrity, and user-visible recovery as applicable. Use `PARITY_VERIFIED` only when those assertions cover the relevant Blazor behavior; do not infer it from route, build, or unit-test success.
