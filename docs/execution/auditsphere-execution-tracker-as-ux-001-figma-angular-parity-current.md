# AS-UX-001 — Angular Visual Adoption Tracker

**Status:** PARTIAL

**Authority:** Tracks implementation evidence for the approved visual direction in [`auditsphere-execution-user-stories-as-ux-001-figma-angular-parity-approved.md`](auditsphere-execution-user-stories-as-ux-001-figma-angular-parity-approved.md). It does not change application behavior, security or the canonical requirements.

## Decision and boundaries

The owner selected the `auditspherefigma` visual language for the existing Angular application. The React prototype is a visual reference only. Angular Material/CDK, current API routes, server-authorized data and AuditSphere's eleven-state lifecycle remain authoritative. The seven visible phases are a read-only projection. No new font, package, route, mock data or business-state mechanism is approved by this direction.

## Current slice

| Story package | State | Evidence and remaining work |
|---|---|---|
| UX-00 baseline/inventory | PARTIAL | Both repository heads were rechecked; the reference prototype rendered locally. The production route-by-route treatment inventory and reproducible synthetic screenshots are not complete. |
| UX-01 tokens/primitives | PARTIAL | Central shell tokens were added to `src/AuditSphereOps.Ui/src/styles.scss`. Shared Material primitives, screen states and contrast review remain. |
| UX-02 header | PARTIAL | Dark header, session-aware workspace label, staff global search, and sign-out are in `src/AuditSphereOps.Ui/src/app/app.html` and `app.scss`; the existing authorized search component/API is retained. Authenticated browser behavior and the search result panel remain unverified. |
| UX-03 navigation | PARTIAL | Existing route groups are styled as a dark sidebar with an in-memory compact mode and accessible names. Role-scoped navigation, mobile dialog behavior and deep-link journeys require authenticated browser evidence. |
| UX-04/05 context and phase ribbon | PARTIAL | Existing seven-phase, server-derived projection uses a responsive grid; no engagement selector or globally visible engagement context was added. Authenticated ribbon comparison remains open. |
| UX-06–12 feature workspaces | NOT STARTED IN THIS STORY | Commercial/onboarding, planning, fieldwork, review, delivery, archive, client portal, practice and finance screens still need route-mapped visual slices. |
| UX-13/14 hardening and responsive/accessibility review | PARTIAL | Existing skip link, focus rules, reduced-motion support and semantic phase status remain. The unauthenticated shell was browser-checked at 1440, 768, 390 and 320 CSS pixels with no horizontal overflow; authenticated, zoom, screen-reader, forced-colors and per-state evidence remain. |
| UX-15 visual sign-off | NOT STARTED | Requires synthetic data, matched screen states, screenshots at the specified viewports and human review. |

## Deliberate visual differences

- No prototype role switcher, mock notifications, hardcoded approvals, local-storage business state, or user-selectable lifecycle phase.
- No external Google Font request. The existing system/Material font stack remains until properly licensed local assets are approved.
- Global search uses its existing authorized API and is now in the header; its staff-only results and failure panel need authenticated browser verification.
- No authenticated local test identity or synthetic engagement was created for screenshot review. The local preview falls through to its sign-in/access-unavailable surface when no API session exists.

## Next eligible slices

1. Complete UX-00 route inventory and capture reference/Angular states using an approved synthetic fixture.
2. Verify UX-02 search results/errors with a staff session and implement UX-04 engagement context only from existing authorized API state.
3. Adapt one representative route per existing workspace, then continue feature-by-feature through UX-06–12.
4. Run the full screen-state, responsive, keyboard, zoom and accessibility review before UX-15 sign-off.

Exact test counts, candidate commit, browser-run details and CI identifiers belong in [`status.json`](status.json); they are not inferred from this tracker.
