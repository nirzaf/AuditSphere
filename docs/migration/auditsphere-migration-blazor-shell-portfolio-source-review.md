# Angular migration source review — shell, search, portfolio and profiles

**Status:** PARTIAL_REVIEWED  
**Source reviewed against code:** `1e22baaeeaf33283bf0ca311a93c1c493bcd82e6`  
**Pinned discovery snapshot:** `eb94ae5073558ec7192ddb5dfd4e24cecd7c4b39`

The six legacy files below match their SHA-256 values in the pinned source
inventory. The source files are retained in the Web rollback/reference host;
their presence and route mapping do not establish parity by themselves.

| Legacy source | SHA-256 |
|---|---|
| `src/AuditSphereOps.Web/Components/Layout/MainLayout.razor` | `3bfa49fb5e1991fc6e32f24ba44484097c0c1036019e7766c0dc2da5f082d663` |
| `src/AuditSphereOps.Web/Components/Layout/GlobalSearch.razor` | `33bbe3e18bb30eec51dc9acd076a56d33b207b705256e7a81958cfc0deea9d55` |
| `src/AuditSphereOps.Web/Components/Pages/Home.razor` | `f7cf54fa3538c2049f3c3a2d23452de8cda47cda00bebfbd3a35a32cedc5d38b` |
| `src/AuditSphereOps.Web/Components/Pages/Portfolio.razor` | `f7771250c924cfe077b5d32413484d0dbaa6c5bfe459651463f6f192d63cb136` |
| `src/AuditSphereOps.Web/Components/Pages/ClientDetail.razor` | `d5489584493ee4c169312c112ea47d8d561dd07bc88b3299484a72eca3117c96` |
| `src/AuditSphereOps.Web/Components/Pages/EngagementDetail.razor` | `dd2c5f09ed6ef2b4e2edcae8f20aa7d81eb04bd60de5f1669ba9d8906ab55f36` |

## Behavior and ownership mapping

| Legacy behavior | Angular, API and Application owner | Current evidence and remaining gap |
|---|---|---|
| Redirect `/` to the staff workspace. | The API root endpoint redirects to Angular's deployment-owned staff destination; Angular `routes` redirects the empty path to `app`, which loads `Portfolio`. | `AngularRouteAndShellMigrationSweepTests.ParameterlessRoutesRenderAndPreserveRoleSpecificShells` includes direct `/` navigation in canonical-route mode. `RootRedirectRespectsPreviewAndCanonicalAngularOwnership` verifies both final targets, `/app` and `/ui/app`, and the Portfolio heading. Its unauthenticated cases verify the sign-in prompt, exact route-mode return destination and arrival at Portfolio after sign-in. Fallback-host behavior remains open. |
| Render shared workspace branding, role-appropriate navigation, route links and a compact navigation drawer. | Angular `App` composes `WorkspaceNavigation` and `WorkspaceNavigationDialog`; `staffGuard` and `clientGuard` govern staff and client routes. | `AngularShellNavigationJourneyTests.ResponsiveMenu_KeyboardFocus_ContextAndRevocation` runs for canonical and `/ui` paths. It checks compact/desktop navigation, keyboard focus and Escape, route focus, browser back/forward, resize behavior, session-epoch revocation, and that client navigation contains only the portal. The full legacy link-by-link destination/denial matrix, screen-reader walk-through and wider locale acceptance remain open. |
| Search the current user's openable clients, engagements, PBC requests, leads, invoices, library and supported pages; keep unsupported documents and email out of the index. | Angular `GlobalSearch` calls `GET /api/ui/search`; `GlobalSearchQuery` owns the bounded query and result projection. The endpoint resolves the trusted actor and rechecks that identity after the query. | `GlobalSearchJourneyTests.StaffSearchFindsOnlyOpenableRecordsAndRespectsKeyboardAndContext` checks an openable scoped client/PBC result, suppression of a sibling-client marker, the stated index boundary, slash/escape behavior, navigation clearing and the compact viewport. At test commit `59c8abc3`, it also searches a published technical-library fixture and opens the exact Angular library route. At `c4eb8c57`, it additionally opens the supported Practice time page result and confirms global search clears after navigation. At `4b019b0c`, seven published library matches produce six displayed links plus the refinement hint, while the first exact result remains openable. A direct API-host journey, `SearchApiEnforcesFirmGrantAndIdentityBoundariesWithoutLeakingCounts`, also checks exact sibling and foreign-firm markers, client-identity denial, no result-count leakage after grant revocation, and stale-session 401. The PostgreSQL-backed `GlobalSearchQueryTests` verifies FinanceManager client isolation, no invoice hits for ordinary Staff, firm-wide FinanceReviewer in-firm visibility, foreign-firm invoice exclusion, and technical-library publication/audience/firm boundaries. At test commit `9c4e24e3`, `ClientEngagementPbcAndInvoiceCapsSignalWhenMoreThanSixMatchesExist` also verifies six authorized results plus `truncated=true` for client, engagement, PBC-request and invoice matches; prior tests cover lead, library and page result caps (client-scoped Staff sees published `ALL_STAFF`, Managers also see `PARTNERS_MANAGERS`, and drafts/other-firm matches are hidden). The API-host test also checks the one-character empty response and 101-character validation error. Per-kind caps/truncation have query evidence across all seven result types. A PostgreSQL-backed Angular browser journey verifies Client → Engagement → PBC request category order, matching the legacy component's kind grouping; legacy relevance scoring is not applicable. `SearchResultKindsFollowCapabilitySpecificRolesAndScopes` additionally verifies CommercialManager client-only, EngagementLeader client/engagement, Auditor engagement/PBC, Accountant PBC-only, FinanceManager invoice-only and RelationshipManager lead-only results. At code commit `5357c887` and test commit `0fa19701`, 31 valid authorized matches for each of Client, Engagement, PBC request and Invoice verify six-hit caps, `truncated=true`, and deterministic tie ordering beyond the 25-row candidate window; the PostgreSQL query cohort passed 16/16 and the Angular search journey passed 2/2. The configured composite foreign key rejects mismatched PBC client/engagement pairs, so no unsupported invalid-row fixture was used. Exhaustive role/scope, broader ranking, other query/error paths and assistive-technology acceptance remain open. |
| Show a scope-limited portfolio, summary counts, recent package/release records, client search/paging, scoped CSV export and deep links. | Angular `Portfolio` serves `/app`; the API provides portfolio summary/workspace/export endpoints. Application portfolio queries and export validation enforce current actor scope. | `AngularPortfolioJourneyTests` covers portfolio denial for a client identity with an erroneous firm-wide Staff grant, scoped client search/paging and records, safe CSV content, return filters, canonical fallback, revocation and export denial. `ScopedSummaryRecordsCsvAndReturnFilters` also directly checks the workspace API's client, engagement, candidate and release aggregates with a populated sibling client. At test commit `abb798ea`, PortfolioWorkspaceTests and PortfolioQueryTests passed 5/5: an exactly 1,000-client CSV export succeeds and 1,001 clients fail with `export.limit`, while narrowed search and CSV neutralization remain covered. At test commit `4fc08ea5`, the PostgreSQL API cohort passed 2/2 and the Angular API/portfolio component tests passed 19/19 across two files. The API returns the typed 400 refusal without a CSV attachment; the Angular view shows it, accepts a narrowed search, reloads the projection and includes the bounded term in the explicit retry request. The successful component retry response is controlled, paired with the separately tested real API refusal. Remaining summary variants, pagination maxima, full role/scope/tenant matrices and additional failure-state coverage remain open. |
| Display client identity and permitted profile details, scoped engagements and contacts, independent paging, portal intent, and guarded contact creation. | Angular `ClientDetail` loads `GET /api/ui/clients/{id}` through `WorkspaceQuery.ClientAsync`; contact creation uses the protected client-contact API and `PracticeCrmService`. The query separately checks client, engagement and contact authority, limits returned rows, and rechecks contributing scopes before returning. | `AngularClientProfileJourneyTests.ScopedMetadataIndependentPagesReloadMobileAndRevocation` runs for canonical and `/ui` routes. It checks profile projection, separate engagement/contact paging across reload, mobile overflow, and indistinguishable unavailable output with prior client data cleared for sibling and random IDs. `AngularClientContactCreationJourneyTests.ReviewPrimaryReplacementDraftLostResponseRecoveryAndRevocation` checks draft restore, reviewed primary-contact replacement, receipt reconciliation after a lost response without duplicate creation, and access-revocation clearing. PostgreSQL-backed `ClientContactCreationTests` checks concurrent idempotency, immutable actor-owned receipts, invalid field samples, stale-generation refusal, foreign-firm/client/role denial, and rollback after revocation during publication. Complete UI validation/conflict rendering, the full role/field-redaction and cross-firm HTTP matrices, and assistive-technology acceptance remain open. |
| Display engagement metadata, current professional-work/hold state, separately authorized client navigation, and only the preparation links allowed by the actor's current grants. | Angular `EngagementDetail` calls `GET /api/ui/engagements/{id}` through `WorkspaceQuery.EngagementAsync`; `EngagementPlanningQuery` owns planning projections and commands. Engagement, client-profile and accounting-preparation authority are checked independently. | `AngularEngagementProfileJourneyTests` checks role/scope-bound preparation links and protected direct routes, paged holds and metadata, route reload/revocation, and that Administrator staffing or approval authority does not confer budget-preparation authority. Full cross-firm/guessed-ID response equivalence, every profile field/error state, and assistive-technology acceptance remain open. |

## Security and composition boundary

Angular uses the same-origin API and authenticated session; the UI does not
query EF Core or call Microsoft Graph. The client and engagement projections
are composed by Application queries, scope their reads by firm and immutable
resource identity, and fail with generic unavailable results when authority
is missing. Advertised client-navigation and preparation capabilities are
authorized separately. The portfolio and search journeys also verify that
unrelated-client markers are not exposed and that revocation clears protected
content.

These protections are evidence from the reviewed current implementations and
focused synthetic journeys; they do not replace the still-open exhaustive
authorization and recovery matrices.

## Local verification

- The focused PostgreSQL-backed Release Playwright cohort passed **16/16**:
  `GlobalSearchJourneyTests`, `AngularShellNavigationJourneyTests`,
  `AngularPortfolioJourneyTests`, `AngularClientProfileJourneyTests` and
  `AngularEngagementProfileJourneyTests`. The exact command, duration and
  cohort boundary are recorded in `docs/execution/status.json`.
- The built-in browser rendered the local Development portfolio, client
  profile and engagement profile, including shared search/navigation and
  scoped workspace data. This was a read-only check; no business command was
  submitted and it is not production acceptance.
- At test commit `6c0b0f49`, the direct API-host search boundary journey passed
  1/1, the full `GlobalSearchJourneyTests` class passed 2/2, and the
  PostgreSQL-backed `GlobalSearchQueryTests` passed 10/10. The direct endpoint
  checks return no hits and `truncated: false` for sibling and foreign-firm
  markers; client identities receive 403; revocation leaves no search hits;
  and an obsolete session epoch receives 401.
- At test commit `672cbfbc`, the PostgreSQL-backed `GlobalSearchQueryTests`
  class passed 11/11. Its invoice case checks FinanceManager client scope,
  ordinary Staff denial, firm-wide FinanceReviewer visibility within the firm,
  and foreign-firm exclusion. The shared build included another agent's
  uncommitted consolidation query edit; these search tests do not exercise it,
  and that file was not staged or committed here.
- At test commit `3511f903`,
  `AngularPortfolioJourneyTests.ScopedSummaryRecordsCsvAndReturnFilters`
  passed 2/2 for canonical and `/ui` route ownership. Direct API assertions
  confirmed one visible client and engagement, 25 ready candidates, one issued
  release, 26 scoped candidates, and no sibling-client marker in the response.
  The shared build included another agent's uncommitted consolidation query
  edit; this portfolio journey does not exercise it and the file was not
  staged or committed here.
- At test commit `c716e2fa`, the full `GlobalSearchJourneyTests` class passed
  2/2 and PostgreSQL-backed `GlobalSearchQueryTests` passed 12/12. Coverage
  includes the one-character empty response, rejection above 100 characters,
  six lead hits with `truncated=true` when a seventh match exists, and the
  previously listed identity, firm, grant and invoice-scope checks. The shared
  Release build included the other agent's uncommitted consolidation query
  edit; these tests do not exercise it and it was excluded from this commit.
- At test commit `c5350bc2`, the new PostgreSQL-backed
  `GlobalSearchQueryTests.TechnicalLibraryHitsRespectAudiencePublicationAndFirmBoundary`
  case passed 1/1 in 21s and the full `GlobalSearchQueryTests` cohort passed
  13/13 in 1m05s. It checks client-scoped Staff and Manager audience results,
  published-only behavior, and same-term firm isolation. This proves the
  query projection; Angular rendering/navigation, full ranking/candidate
  limits and exhaustive role/scope matrices remain open. The shared build
  included another agent's uncommitted consolidation query edit, which these
  tests do not exercise and which was excluded from this commit.
- At test commit `59c8abc3`, the full `GlobalSearchJourneyTests` browser cohort
  passed 2/2 in 45s. A scoped Staff user sees the published `ALL_STAFF` library
  result, opens `/app/library/{id}`, and has the global-search panel cleared on
  navigation. Manager-only/draft/foreign-firm filtering is covered by the
  separate PostgreSQL query test above; ranking/candidate limits and exhaustive
  role/scope matrices remain open.
- At test commit `c4eb8c57`, `GlobalSearchJourneyTests` passed 2/2 in 48s after
  adding a supported-page result journey. The same scoped Staff user opens the
  Practice time page through its search result and the global-search panel
  clears. Full ranking/candidate boundaries and exhaustive role/scope remain
  open.
- At test commit `a67e6a06`, `GlobalSearchQueryTests` passed 14/14 in 1m08s
  and `GlobalSearchJourneyTests` passed 2/2 in 48s. The PostgreSQL test
  reproduced and now verifies `truncated=true` at six returned page and
  technical-library hits when more than six matches exist. The existing lead
  cap remains covered. Caps/ranking for client, engagement, PBC and invoice
  results and exhaustive role/scope matrices remain open.
- At test commit `4b019b0c`, `GlobalSearchJourneyTests` passed 2/2 in 47s with
  seven published technical-library entries. The Angular UI shows six results
  and the refinement hint, then opens the selected library entry. This pins
  the API truncation flag to its visible UI state; page-only overflow now has
  matching visible-hint coverage, while candidate-window exhaustion, ranking
  and exhaustive roles/scopes remain open.
- At test commit `9c4e24e3`, `GlobalSearchQueryTests` passed 15/15 in 1m16s.
  A PostgreSQL-backed case seeds seven authorized records for each of Client,
  Engagement, PBC request and Invoice and verifies six returned hits per kind
  with `truncated=true`. Together with the existing lead, library and page
  cases, per-kind result caps are covered across all seven search types.
- At test commit `477a953f`, the Angular search component suite passed 4/4,
  and full Angular CI passed 495/495 across 95 files. Added assertions cover
  the 15-second timeout's safe retry state and clearing/cancellation on session
  invalidation. The Angular production build passed with the existing
  `settings.scss` component-style budget warning (7.53 kB against 4.00 kB;
  349.67 kB initial bundle). Component tests do not replace browser or
  assistive-technology acceptance; candidate-window and
  exhaustive role/scope evidence remain open.
- At test commit `8c220032`, the PostgreSQL-backed Angular browser journey
  verifies that a cross-kind `pbc test` search returns Client, Engagement,
  then PBC request hits. This matches the legacy component's grouping by kind
  in first-occurrence order; the legacy component did not apply relevance
  scoring. Candidate-window boundary cases, complete role/scope coverage and
  assistive-technology acceptance remain open.
- At test commit `df8a62f5`, `GlobalSearchQueryTests` passed 16/16 in 1m19s.
  `SearchResultKindsFollowCapabilitySpecificRolesAndScopes` verifies twelve
  role/scope combinations, including client-only, engagement-only, PBC-only,
  finance-only and leads-only access. It covers CommercialManager,
  EngagementLeader, Auditor, Reviewer, Accountant, AccountingPreparer,
  AccountingReviewer, FinanceManager, FinanceReviewer, RelationshipManager,
  Senior and Administrator. These are representative cases; exhaustive
  role/scope coverage remains open.
- At code commit `5357c887`, `GlobalSearchJourneyTests` passed 2/2 in 49s. At
  test commit `0fa19701`, `GlobalSearchQueryTests` passed 16/16 in 1m21s. A
  PostgreSQL fixture seeds 31 valid authorized matches for each of Client,
  Engagement, PBC request and Invoice; it verifies six results, `truncated=true`
  and deterministic tie ordering beyond the 25-row candidate window. The
  configured composite foreign key rejects mismatched PBC client/engagement
  links. Broader ranking, exhaustive role/scope, other query/recovery paths and
  assistive-technology acceptance remain open.
- The built-in Development browser smoke returned five authorized synthetic
  Client, Engagement and PBC hits. It was read-only and is not production
  acceptance.
- At test commit `abb798ea`, `PortfolioWorkspaceTests` and
  `PortfolioQueryTests` passed 5/5 in 37s. Exactly 1,000 client rows export;
  1,001 are refused with `export.limit`, while narrowed search still exports.
  Formula-prefix, quote and multiline escaping remain checked. This test-only
  slice did not change API or EF behavior.
- At test commit `4fc08ea5`, `PortfolioWorkspaceApiTests` passed 2/2 with a
  firm-wide Administrator and 1,001-client fixture. The refused export returns
  HTTP 400 with `export.limit` and the safe narrowing message, and no CSV
  attachment. The same commit's Angular `api.spec.ts` and
  `portfolio-workspace.spec.ts` passed 19/19 across two files. After the
  refusal, the component test changes the filter, reloads the narrowed
  projection and confirms the single export retry uses that search before
  clearing the error on a successful controlled response. This remains a
  focused test slice; a combined full browser error/recovery journey, full
  solution suite and EF check remain open.
- The existing `AngularClientContactCreationJourneyTests` cohort passed 2/2
  on 2026-10-05 for canonical and `/ui` route ownership. It verifies restored
  tab drafts, exact primary-contact review, lost-response receipt recovery,
  one persisted contact-creation record, and clearing after Manager grant
  revocation. Full UI validation/conflict rendering, exhaustive role,
  field-redaction, cross-firm and assistive-technology coverage remains open.
- The PostgreSQL-backed `ClientContactCreationTests` cohort passed 4/4 on
  2026-10-05. It exercises concurrent same-key creation, immutable receipt
  lookup, changed-request conflict, invalid field samples, stale-generation
  refusal without side effects, client/role/foreign-firm receipt denial, and
  rollback when Manager authority is revoked during publication. Complete UI
  validation/conflict rendering and exhaustive role, field-redaction,
  cross-firm and assistive-technology matrices remain open.
- At test commit `c3a9ad21`,
  `AngularRouteAndShellMigrationSweepTests.RootRedirectRespectsPreviewAndCanonicalAngularOwnership`
  passed 2/2 for the signed-in canonical and preview redirect targets.
- At test commit `be9d736d`, the same two-mode browser cohort passed 2/2 in
  39s after adding unauthenticated starts. Both modes show the sign-in prompt,
  preserve the exact return destination (`/app` or `/ui/app`), and reach
  Portfolio after sign-in. Fallback-host behavior remains open.
- The complete solution regression and EF model-drift check were not run for
  this slice. The latest complete-solution result remains the earlier
  1001/1001 checkpoint in `status.json`.

All six artifacts remain `PARTIAL`. Remaining role, field, cross-firm,
guessed-ID, validation, error/recovery, accessibility and assistive-technology
coverage prevents a parity claim. Production/canary, owner and external
acceptance remain separate gates. Blazor retirement is still `NOT_READY`.
