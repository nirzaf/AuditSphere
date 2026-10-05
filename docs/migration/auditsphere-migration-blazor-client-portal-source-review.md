# Blazor Client Portal Home Source Review

**Status:** PARTIAL
**Legacy source:** `src/AuditSphereOps.Web/Components/Pages/ClientPortal.razor`
**Pinned SHA-256:** `32f612c79034a468ae097e055a9b0554036cc500c28fdaa6036803c69fbe76d1`

## Replacement and behavior

The legacy `/portal` home page is replaced by Angular `features/portal/home.ts`,
with shared documents, request conversation/upload and validated package detail
provided by `features/portal/documents.ts`, `features/portal/request.ts`, and
`features/portal/package.ts`. Same-origin endpoints compose the scoped
Application queries and commands; the Angular page does not query the database
or call Microsoft Graph.

The ClientPortal home source/action row is now mapped to current implementation
and executable evidence:

- The bounded API query validates an active client identity and current
  assigned engagements, returns only participant requests, rechecks the
  engagement and assignment snapshot, and caps financial packages at the
  latest 100 validated packages.
- Request pagination now accepts only 10, 25, or 50 entries. The Angular home
  defaults to 10, allows all three legacy page-size choices, and asks the API
  for the matching page. Financial packages expose the same page-size choices
  and pagination across the server-bounded 100-package result.
- When onboarding is pending and no engagement is active, Angular now displays
  the pending message by itself, matching the legacy state and avoiding empty
  request, review, and package workbenches before access opens.
- The existing portal feature continues to expose first-sign-in
  acknowledgement, assigned requests, shared-document review/comments,
  management-signed representation upload, final deliverable bundles, and
  validated financial packages. The staff journal-management addition is a
  separate feature and does not grant client users staff authority.

## Verification

- The Angular home unit suite passed **2/2**. It verifies request and package
  page sizes 10/25/50, server page requests and boundaries, package ranges, and
  the pending-onboarding-only view.
- Angular CI passed **491/491** across 95 test files. The production build
  passed with a 349.67 kB raw initial bundle; the existing Commercial Settings
  stylesheet warning remains at 7.53 kB against its 4 kB warning budget.
- PostgreSQL-backed `ClientPortalOnboardingTests` passed **3/3**, including
  10-row page boundaries, next-page isolation, the 25-row choice, sibling
  participant denial and rejection of unsupported page sizes.
- API `UiPortalContractTests` passed **2/2**; the portal endpoint binds the
  selected page size, refuses unsupported size 20, requires CSRF on commands,
  withholds upload capabilities from reads, and denies staff access.
- `AngularClientPortalJourneyTests` passed **1/1** through the API-host browser:
  first sign-in, primary-contact delegation/revocation, conversation reply,
  browser-side file hashing and upload staging, guessed request denial,
  disabled-user/session revocation clearing, and a 390px viewport.
- The built-in Development browser was signed in as the staff-only
  `Workspace preview` identity. Navigating to `/ui/portal` returned it to the
  staff portfolio; no client portal data was exposed. The client path was
  exercised with the synthetic client identity in the API-host browser test.

## Remaining gaps

This source row remains **PARTIAL**. The focused journey does not close the
complete client role/scope and cross-firm matrices, shared-document comment and
acknowledgement recovery, signed-representation upload validation/recovery,
final-bundle authorization, every stale/error state, or human assistive-
technology and wider-locale acceptance. The focused changes did not alter the
EF model. The full solution regression and EF pending-model check were not run;
the latest complete solution checkpoint remains 1001/1001 at `ead85032`.
Production/canary, live Microsoft and separate owner acceptance remain open;
the Blazor rollback host stays required and migration retirement remains
`NOT_READY`.
