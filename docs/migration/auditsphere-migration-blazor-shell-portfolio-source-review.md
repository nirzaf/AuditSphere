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
| Redirect `/` to the staff workspace. | Angular `routes` redirects the empty path to `app`; `/app` loads `Portfolio`. | The portfolio journeys exercise the resulting workspace through both canonical and `/ui` route ownership. A direct root-navigation and unauthenticated fallback-host matrix is not part of the focused cohort. |
| Render shared workspace branding, role-appropriate navigation, route links and a compact navigation drawer. | Angular `App` composes `WorkspaceNavigation` and `WorkspaceNavigationDialog`; `staffGuard` and `clientGuard` govern staff and client routes. | `AngularShellNavigationJourneyTests.ResponsiveMenu_KeyboardFocus_ContextAndRevocation` runs for canonical and `/ui` paths. It checks compact/desktop navigation, keyboard focus and Escape, route focus, browser back/forward, resize behavior, session-epoch revocation, and that client navigation contains only the portal. The full legacy link-by-link destination/denial matrix, screen-reader walk-through and wider locale acceptance remain open. |
| Search the current user's openable clients, engagements, PBC requests, leads, invoices, library and supported pages; keep unsupported documents and email out of the index. | Angular `GlobalSearch` calls `GET /api/ui/search`; `GlobalSearchQuery` owns the bounded query and result projection. The endpoint resolves the trusted actor and rechecks that identity after the query. | `GlobalSearchJourneyTests.StaffSearchFindsOnlyOpenableRecordsAndRespectsKeyboardAndContext` checks an openable scoped client/PBC result, suppression of a sibling-client marker, the stated index boundary, slash/escape behavior, navigation clearing and the compact viewport. Every result kind, query/error state, complete role/scope matrix and assistive-technology acceptance remain open. |
| Show a scope-limited portfolio, summary counts, recent package/release records, client search/paging, scoped CSV export and deep links. | Angular `Portfolio` serves `/app`; the API provides portfolio summary/workspace/export endpoints. Application portfolio queries and export validation enforce current actor scope. | `AngularPortfolioJourneyTests` covers portfolio denial for a client identity with an erroneous firm-wide Staff grant, scoped client search/paging and records, safe CSV content, return filters, canonical fallback, revocation and export denial. All summary and record variants, maximum boundaries, full role/scope/tenant matrix, and export failure/unknown-result behavior remain open. |
| Display client identity and permitted profile details, scoped engagements and contacts, independent paging, portal intent, and guarded contact creation. | Angular `ClientDetail` loads `GET /api/ui/clients/{id}` through `WorkspaceQuery.ClientAsync`; contact creation uses the protected client-contact API and `PracticeCrmService`. The query separately checks client, engagement and contact authority, limits returned rows, and rechecks contributing scopes before returning. | `AngularClientProfileJourneyTests.ScopedMetadataIndependentPagesReloadMobileAndRevocation` runs for canonical and `/ui` routes. It checks profile projection, separate engagement/contact paging across reload, mobile overflow, and indistinguishable unavailable output with prior client data cleared for sibling and random IDs. The complete contact-create validation/recovery journey, every role/field-redaction case, and cross-firm HTTP denial matrix remain open. |
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
- The complete solution regression and EF model-drift check were not run for
  this slice. The latest complete-solution result remains the earlier
  1001/1001 checkpoint in `status.json`.

All six artifacts remain `PARTIAL`. Remaining role, field, cross-firm,
guessed-ID, validation, error/recovery, accessibility and assistive-technology
coverage prevents a parity claim. Production/canary, owner and external
acceptance remain separate gates. Blazor retirement is still `NOT_READY`.
