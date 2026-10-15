# AS-UX-001 — Adopt the AuditSphere Figma-Style Design in the Production Angular Application

**Document type:** Technical implementation user story / coding-agent execution prompt
**Status:** APPROVED FOR INCREMENTAL IMPLEMENTATION — visual direction approved by the owner on 2026-10-15; implementation and acceptance remain partial
**Prepared:** 15 October 2026
**Target repository:** [`nirzaf/AuditSphere`](https://github.com/nirzaf/AuditSphere), `master` at `b96c8f8cc23a10c54ff63301132c9b0a11c15231`
**Design-reference repository:** [`nirzaf/auditspherefigma`](https://github.com/nirzaf/auditspherefigma), `main` at `c1202ef8751e3465d380735bf76b27085cb38bd6`
**Canonical frontend:** Angular 22, Angular Material/CDK, TypeScript, SCSS
**Canonical backend:** Existing ASP.NET Core/.NET modular monolith; PostgreSQL; same-origin `/api/ui/*`
**Applicable functional authority:** STE Audit Management Specification v2.2, current requirements/ADRs, and repository `AGENTS.md`
**Priority:** High (visual adoption); feature-by-feature delivery; no big-bang replacement

**Execution baseline revalidated:** AuditSphere `master` / `origin/master` at `fe9c949e5a085477710b6bce86d3acb42ccbbb28` before the first UX implementation changes. The design reference remains pinned at `c1202ef8751e3465d380735bf76b27085cb38bd6`. The original prepared target baseline below is retained as source history.

> **Important scope distinction:** `auditspherefigma` is a **Next.js/React design prototype**, not a production Angular implementation and not necessarily a native `.fig` document. It contains simulated roles, hardcoded examples, and browser-local state. Reproduce its approved *visual design and navigational experience*, **not** its mocked business logic, state model, or technologies. This story changes presentation. It does not declare that unrelated production-readiness or professional-methodology gaps are completed.

---

## 1. Executive user story

**As** an auditor, senior, manager, Partner, practice administrator, or authorized client using AuditSphere,
**I want** the existing operational application to have the professional, compact, intuitive visual appearance of the `auditspherefigma` reference—including its dark navigation, clear engagement context, seven-phase ribbon, modern cards/tables, consistent actions, and readable audit-specific workspaces—
**so that** I can locate clients and engagement tasks quickly, understand where work is blocked, complete activities with fewer navigation steps, and keep confidence that every action uses the existing authorized accounting, audit, and commercial workflow.

### Primary outcome

An audit user should recognize the design reference's **layout, hierarchy, colors, spacing, density, typography, navigation, and phase presentation** when using AuditSphere's live Angular workspaces, without losing any real functionality. The seven-phase model remains a **read-only projection** of the existing eleven canonical lifecycle states; it is not a replacement state machine.

### Two independent acceptance dimensions

1. **Visual/UX parity:** An approved visual comparison matrix and authenticated screenshots show the equivalent layout and interaction patterns at agreed desktop/tablet/mobile viewports; any deliberate difference is documented and approved.
2. **Functional/security non-regression:** Every existing route, role authorization boundary, client scope, API command, accounting precision rule, approval gate, session invalidation rule, deep link, and saved-draft behavior remains intact or stronger.

**Never report “100% done” based only on a visual resemblance, a green Angular build, or a checked-off screen list.** Completion requires the acceptance contract in Section 13.

---

## 2. Verified starting point and source references

The following findings were established by reading the two repositories' code. They are **code observations**, not claims of side-by-side browser rendering or pixel-perfect parity.

| Category | `auditspherefigma` reference | Existing AuditSphere production UI | Current assessment |
|---|---|---|---|
| Technology | Next.js 15, React 19, Tailwind CSS, React state/context, Lucide | Angular 22, Material/CDK, SCSS, signals, Angular Router | Different stacks; **do not migrate** |
| Shell | Dark `slate-900` header and collapsible dark sidebar; light `slate-100` canvas | Blue-gradient header and light-gray sidebar | Visually different; target for redesign |
| Navigation | Sidebar with phase-specific links, engagement switcher, header search | Existing permission-conscious `WorkspaceNavigation` and route catalogue | Preserve route and scope authority; restyle |
| Progress | Compact horizontal seven-phase global ribbon | Seven-phase in-page workflow stepper and separate 11-state lifecycle panel | Reuse the canonical phase query; change its placement/presentation carefully |
| Financial dashboard | Split upper P&L/lower balance sheet, FSLI drill-ins | Real split statement view, source contributions, review/procedure links | Functional counterpart exists; restyle |
| Work/practice | Workflow board, calendar, templates, ledger and hours demo | Real My Work, board/calendar, workflow templates, firm ledger, time and analytics | Restyle real workflows rather than adopting mock state |
| Client portal | Interactive simulated portal | Scoped Angular client portal and genuine `/api/ui/*` requests | Restyle without expanding client data access |
| Persistence | Hardcoded `INITIAL_ENGAGEMENTS`, `localStorage` in `lib/store.tsx` | API-backed application state, scoped sessions and persisted records | Prototype persistence **must not be imported** |
| Typeface | `Plus Jakarta Sans`, `JetBrains Mono` Google Fonts import | Installed system font stack with Material 3 | Font adoption needs owner-approved sourcing and offline fallback |
| Tests | Reference includes prototype code | Angular specs, Playwright/.NET E2E, route contracts, backend authorization tests | Extend existing tests, never weaken them |

### Exact reference files

**Design repository** (pin to the reference commit above):

- [`app/page.tsx`](https://github.com/nirzaf/auditspherefigma/blob/c1202ef8751e3465d380735bf76b27085cb38bd6/app/page.tsx) — shell composition and screen selection.
- [`app/globals.css`](https://github.com/nirzaf/auditspherefigma/blob/c1202ef8751e3465d380735bf76b27085cb38bd6/app/globals.css) — reference fonts, cards and scrollbar character.
- [`components/layout/HeaderBar.tsx`](https://github.com/nirzaf/auditspherefigma/blob/c1202ef8751e3465d380735bf76b27085cb38bd6/components/layout/HeaderBar.tsx) — header, context switcher, search, notifications.
- [`components/layout/Sidebar.tsx`](https://github.com/nirzaf/auditspherefigma/blob/c1202ef8751e3465d380735bf76b27085cb38bd6/components/layout/Sidebar.tsx) — collapsible dark sidebar and folder metaphor.
- [`components/layout/ProgressRibbon.tsx`](https://github.com/nirzaf/auditspherefigma/blob/c1202ef8751e3465d380735bf76b27085cb38bd6/components/layout/ProgressRibbon.tsx) — seven-stage horizontal progress ribbon.
- `components/screens/Screen01Onboarding.tsx` through `Screen09PracticeLedger.tsx`, `ScreenWorkflowDashboard.tsx`, and `ScreenPracticeTemplates.tsx` — visual workbench references.
- `components/modals/{ReceiptVoucher,HoldingLetter,InspectionExport,ReviewNote,Timesheet,DocumentPreview}Modal.tsx` — interaction styling references only.
- [`lib/mock-data.ts`](https://github.com/nirzaf/auditspherefigma/blob/c1202ef8751e3465d380735bf76b27085cb38bd6/lib/mock-data.ts) and [`lib/store.tsx`](https://github.com/nirzaf/auditspherefigma/blob/c1202ef8751e3465d380735bf76b27085cb38bd6/lib/store.tsx) — **explicitly prohibited as business-state implementations**.

**Target repository** (reinspect at work start because `master` may advance):

- `src/AuditSphereOps.Ui/src/styles.scss` — shared Material theme and `--as-*` design tokens.
- `src/AuditSphereOps.Ui/src/app/{app.html,app.scss,app.ts}` — current shell, responsive navigation, search and session handling.
- `src/AuditSphereOps.Ui/src/app/{navigation.ts,app.routes.ts}` — navigation and real route catalogue.
- `src/AuditSphereOps.Ui/src/app/core/{workspace-navigation.ts,session.ts,api.ts,decode.ts,ui.ts,drafts.ts,tab-drafts.ts}` — reusable safe presentation primitives.
- `src/AuditSphereOps.Ui/src/app/features/engagements/{engagement-workflow.*,engagement-lifecycle.*,engagement.*,statements.*,planning.ts}` — key engagement UX.
- `src/AuditSphereOps.Ui/src/app/features/{audit,portal,practice,finance,commercial,accounting,portfolio}` — existing specialized workspaces.
- `src/AuditSphereOps.Api/Ui/UiEndpoints.Infrastructure.cs` — `SpaRoutes` catalogue; do not bypass.
- `docs/architecture/auditsphere-angular-conventions-current.md` and `docs/architecture/auditsphere-angular-migration-current.md` — current frontend authority.
- `AGENTS.md`, architecture ADR register, approved STE v2.2 requirements, execution task board and `docs/execution/status.json` — workflow/nonregression authority.

### Mandatory baseline check before implementation

The coding agent must verify both remote HEADs and inspect the live target file structure. If newer code has already implemented a requested UX change, **credit and test it rather than reimplementing it**. If the reference design has also changed, pin the intended version or seek owner confirmation before silently changing the target style.

---

## 3. Scope, architecture and non-goals

### In scope

- A coherent visual design system built on the **existing** Angular Material/CDK and SCSS.
- Professional dark sidebar, header, consistent search/context controls and responsive layout.
- Contextual seven-phase ribbon and workflow summaries backed by the **existing** phase/readiness projection.
- Visual consistency of the seven engagement phases and representative portal, firm-practice and accounting workspaces.
- Reusable accessible cards, tables, status chips, money presentation, dialogs, skeleton/empty/error states and action-group patterns.
- Better information architecture for firm-wide versus engagement-specific navigation.
- Keyboard, screen-reader, focus, zoom, mobile and reduced-motion support.
- Behavior-preserving, test-backed incremental implementation and evidence-based visual comparison.

### Explicit non-goals

- **No** React, Next.js, Tailwind, React context/store, `localStorage` mock workflow, prototype role selector, or second SPA.
- **No** backend/domain rewrite, new approval state machine, new accounting engine, changed regulatory gates or automatic audit opinion.
- **No** fabricated payment statuses, fake document uploads, simulated signing, mocked customer data in production, or status changes by clicking a colored phase.
- **No** broad route renames or API contract rewrites just to match prototype wording.
- **No** new dependencies (including icons, charts, animation libraries or font packages) without explicit owner approval.
- **No** replacement of production role/scope permissions with prototype role switching.
- **No** removal of the retained Blazor reference/rollback host; it has an independent migration-readiness gate.
- **No** client-accounting or firm-ledger mixing: these remain separate authority boundaries.
- **No** eSignature provider, Purview, tenant-wide Graph scopes, background job architecture change or new product modules.
- **No** unapproved new source assets; confirm asset/font licensing before copying or bundling.

### Mandatory product invariants

1. Existing five functional modules remain; the eleven canonical lifecycle states remain authoritative; seven user phases are display-only.
2. Only server-authorized Application commands can record approvals, payments, reviews, releases and archive locks.
3. Retain exact scoped `RoleGrant` and authenticated-session checks. Hidden UI does not replace server authorization.
4. Preserve antiforgery, same-origin `/api/ui/*`, bounded response decoders and session-generation fencing.
5. Preserve source/revision hashes, idempotency, currentness, auditable immutability and uncertain-operation recovery.
6. Preserve exact monetary decimal **strings** (no conversion through JavaScript `Number` for financial calculations).
7. Preserve original accessible controls, validation, error messages, and task/approval distinctions even if visuals change.
8. Preserve `app.routes.ts` ↔ API `SpaRoutes` parity, route guards and browser-reload deep links.
9. Preserve working screens and existing tests. Never delete tests or relax assertions to manufacture parity.
10. Visually hide unauthorized data/actions, but never render server-denied content before authorization settles.

---

## 4. Visual design contract

Use **the prototype** as visual reference, **the existing Angular conventions** as the engineering contract and **the actual approved STE requirements** as the behavior contract.

### 4.1 Core visual language

| UI element | Target direction | Implementation constraint |
|---|---|---|
| Shell header | Deep charcoal/slate with compact branded AuditSphere mark and engagement context | Keep existing sign-out, session and accessibility behavior |
| Navigation sidebar | Dark vertical navigation; 16rem expanded baseline; clear active states; optional collapse | Backed by existing route catalogue and session scope; no new fake links |
| Canvas | Soft gray `slate-100`-like background; readable white cards | Maintain WCAG AA and visible focus |
| Primary accent | Professional blue, approximately prototype `blue-600` | Centralize tokens; do not sprinkle unrelated hex values |
| Borders/radii | Subtle 1px borders; modest 8–12px radii | Do not trade contrast for aesthetics |
| Elevation | Low/no shadows for most cards; stronger only for overlays/active controls | Distinguish interactive from informational surfaces |
| Information density | Compact but legible; aligned figures, clear sections, restrained badges | Keep 44px practical targets on touch where applicable |
| Data tables | Horizontal scroll in dense financial views; sticky headers where compatible | Preserve total rows, pagination, row identity and keyboard access |
| Typography | Prototype-inspired humanist sans; tabular figures/monospace for IDs/QAR | Use system font fallback until licensed/local font asset approval |
| Status | Clear text + icon/shape + accessible color | Never color-only; status always derives from authoritative data |
| Loading | Skeleton/progress using current async loading contract | No fake completed status while API pending |
| Animation | Subtle CSS transitions | Respect `prefers-reduced-motion` and avoid blocking transitions |

**Recommended initial token candidates** (approximate Tailwind semantics, not final measured design values):

```scss
// Integrate into the existing --as-* token system; names and final values are subject to visual review.
:root {
  --as-shell-dark: #0f172a;        // slate-900
  --as-shell-dark-raised: #1e293b; // slate-800
  --as-shell-dark-border: #334155; // slate-700
  --as-canvas: #f1f5f9;           // slate-100
  --as-card: #ffffff;
  --as-action-primary: #2563eb;   // blue-600
  --as-shell-header-height: 4rem;
  --as-sidebar-expanded-width: 16rem;
}
```

These **extend/adjust the existing Material theme and SCSS tokens**. Keep current focus, contrast and color-scheme safeguards. Verify colors against the real reference screenshots; do not hard-code this draft as proof of exact pixel parity.

### 4.2 Typography and images

- Prototype imports `Plus Jakarta Sans` and `JetBrains Mono` from Google Fonts; production currently avoids external font downloads. Either obtain approval for **properly licensed local assets** or retain a compatible installed/system fallback and record the deliberate visual variance. Do not introduce runtime external font fetches or undocumented font-file distribution.
- Use `font-variant-numeric: tabular-nums` for amounts, percentages, date columns and financial table footers; continue using existing exact-money pipes.
- Reuse approved existing logo/assets; if the prototype's square `AS` mark is selected, implement it as a small accessible Angular template/CSS treatment, not a dependency.
- Reuse Material/CDK/icon facilities already installed; do not install `lucide-react` into Angular. If a particular icon is unavailable, use an accessible SVG with ownership/licensing verified or retain a text label.

### 4.3 Visual states to baseline

At minimum capture reference and target for: normal data, empty, loading, API failure, access denied, blocked gate, waiting for client, returned for rework, reviewed, and locally archived. A polished happy-path screenshot alone is insufficient.

---

## 5. Screen mapping and interaction contract

The following are **reference-design → existing production destination** mappings. They do **not** authorize replacing a protected action with a prototype toggle.

| Prototype component | Real Angular entry point(s) to adapt | Treatment |
|---|---|---|
| `HeaderBar.tsx`, `Sidebar.tsx` | `app.html`, `app.scss`, `core/workspace-navigation.ts`, `navigation.ts`, `features/search/*` | New look; existing authenticated scope/route behavior |
| `ProgressRibbon.tsx` | `features/engagements/engagement-workflow.{ts,html,scss}`, engagement page, shell where context exists | Server-derived ribbon; no second phase engine |
| `Screen01Onboarding.tsx` | `features/commercial/*`, `features/acceptance/*`, `features/engagements/activation.*`, `engagement-onboarding-readiness.*` | Dual key, advance, client context as cards; existing business commands |
| `Screen02Planning.tsx` | `features/engagements/planning.ts`, `features/practice/resources.*`, TB intake and audit planning | Capacity, deadlines, TB and materiality in a consistent workbench |
| `Screen03Fieldwork.tsx` | `features/engagements/statements.*`, `features/audit/{fieldwork,workpaper,plan}.*` | Visual FSLI split and drilldown; keep current source/approval identities |
| `Screen04Review.tsx` | `features/audit/{fieldwork,confirmations,completion}.*`, `features/accounting/reviews.*`, review notes | Structured Manager review and rework, no new authority |
| `Screen05PartnerSignOff.tsx` | `features/audit/completion.ts`, report opinions/signing views | Prominent decision eligibility, basis, blocker and artifact version |
| `Screen06Deliverables.tsx` | `features/audit/completion.ts`, `features/accounting/package.ts`, release/finance screens | Five-part delivery cards and exact report versions |
| `Screen07Archive.tsx` | `features/audit/records.ts`, archive detail and completion freeze | Signature-anchored countdown, actual local lock, separate provider state |
| `Screen08ClientPortal.tsx` | `features/portal/{home,request,package,documents,finance}.*` | Lighter restricted client shell, no internal audit leaks |
| `Screen09PracticeLedger.tsx` | `features/finance/ledger.ts`, `features/practice/{time,analytics,rate-cards}.*` | High-density finance layout, correct firm vs client ledger |
| `ScreenWorkflowDashboard.tsx` | `engagement-workflow.*`, `features/practice/{work-queue,work-queue-views}.*` | Real tabs/board/calendar/queues, not copied demo tasks |
| `ScreenPracticeTemplates.tsx` | `features/practice/workflow-templates.ts`, `features/engagements/engagement-task-plan.ts` | Existing revisioned templates and authentic task instantiation |
| `ScreenArchitectureMap.tsx` | Documentation/internal project-progress view, **not** a new required production screen | Design/documentation reference only unless separately approved |
| Prototype dialogs | Existing Angular Material dialogs or on-page commands | Reuse real commands, preserve independent review and modal focus |

**No forced 1:1 route requirement:** A single prototype screen may correspond to several production workspaces with distinct authorization. Consolidate visually only where doing so does not merge authorities, create giant components, or increase task complexity.

---

## 6. Detailed implementation stories and acceptance criteria

The numbered packages below are **implementation slices**. Close each on evidence before moving to the next. The target agent should prefer the smallest code diff that produces the required result.

### UX-00 — Capture baseline, inventory and sign off visual references

**Priority:** P0 · **Dependencies:** none · **Target:** reference dossier, no business code changes.

**Work**

- Pin both repository commits, capture the prototype route/screen catalogue and the current Angular route catalogue.
- Run both development UIs locally under documented safe settings, if available. Use only synthetic data in visual reference images; never include sensitive client/account data in screenshots.
- Produce reference screenshots for desktop 1440×900, tablet 768×1024, mobile 390×844 and narrow 320px, adjusting viewports only with recorded rationale.
- Record each prototype screen's header, sidebar, phase ribbons, content max width, fonts, colors, spacing, table density and interactive states. Capture keyboard/focus states separately.
- For every Angular route, mark `SHELL_ONLY`, `CORE_SCREEN`, `DENSE_FINANCIAL`, `CLIENT_PORTAL`, `AUTH/SETUP`, or `EXEMPT`, so no route is silently skipped.
- Agree that the prototype is a *style reference*, not a policy source. Record intentional UX deviations (especially role switcher, prototype toggles, mock notifications and simulated approvals).

**Acceptance criteria**

- **UX-00-01** Both exact code baselines and latest remote heads are recorded before change.
- **UX-00-02** All production routes are inventoried and assigned a treatment and owner; unknown/missing screens remain explicit gaps.
- **UX-00-03** Reference screenshots and variants are reproducible or explicitly marked `NOT_VERIFIED` if the reference cannot be run.
- **UX-00-04** No client data or sensitive secrets are present in screenshot artifacts.
- **UX-00-05** Visual direction and deliberate differences are approved before broad implementation.

### UX-01 — Establish cohesive Angular design tokens and reusable primitives

**Priority:** P0 · **Dependencies:** UX-00 · **Files:** `styles.scss`, existing `core/ui.ts`, only small justified shared components.

**Work**

- Translate the prototype's dark-shell/light-canvas design to a small set of `--as-*` SCSS variables.
- Update Material theme surfaces, button appearances, input alignment, dialog/popup surfaces, neutral typography and interactive states using supported theming.
- Standardize `audit-page-header`, `audit-state`, `audit-command-message`, `audit-status`, card/panel, tables and action controls; retain business-specific controls within their existing feature pages.
- Keep a single semantic color vocabulary: normal/info, success/approved, warning/pending, blocked/error, neutral/not-applicable. Never make fake approved colors.
- Honor high contrast, browser zoom, reduced motion, loading error/empty states, and overlay surface contrast.
- Make spacing/density defaults adaptable: shell comfortable, financial grids compact. Avoid a global compressed control setting that makes touch/caret targets unusable.

**Acceptance criteria**

- **UX-01-01** Shared tokens are centralized; no duplicate competing palette files or Tailwind installation.
- **UX-01-02** Material dialog/overlay/control colors and typography remain legible over both dark shell and light cards.
- **UX-01-03** Visible keyboard focus is retained for links, controls, menus, dialogs and row actions.
- **UX-01-04** Amounts continue to use existing exact-string and tabular-numeric conventions.
- **UX-01-05** Existing form-field, page-header and status component contract tests remain equal-strength or stronger.
- **UX-01-06** Font decision is documented; an external runtime font dependency is not introduced silently.

### UX-02 — Rebuild the app header without changing session authority

**Priority:** P0 · **Dependencies:** UX-01 · **Files:** `app.{html,scss,ts}`, existing search/session components.

**Work**

- Reproduce the compact dark top bar: accessible brand, current work-area label, engagement context when appropriate, global search, appropriate notifications/pending outcomes and sign-out.
- Preserve existing live session verification and sign-out behavior. Do not draw authenticated content while session is unknown.
- Retain the existing global search implementation; only move/restyle it. If keyboard shortcuts already exist, preserve them; otherwise add shortcuts only with accessible focus handling and no interference with form typing.
- Replace prototype's user-role selector with an actual current-user role/capability summary **only if** that information is already authorized and available. No client-side role switching.
- Show notifications only from real authorized sources. Where unread-notification data is unavailable, omit the badge rather than fabricate a count.
- Make a deliberate placement for `audit-pending-outcomes`: an uncertain operation remains visible and recoverable after redesign.
- Keep `/auth`, `/setup` and sign-in flows segregated from authenticated navigation.

**Acceptance criteria**

- **UX-02-01** Header composition materially resembles prototype while preserving the real sign-in/out flow.
- **UX-02-02** A staff user sees only server-authorized context; a client never sees staff navigation or operational data.
- **UX-02-03** A revoked/changed session invalidates visible protected content and closes transient controls.
- **UX-02-04** Search retains current API scope, bounded loading/error states, keyboard usage and deep links.
- **UX-02-05** Pending/unknown operation banners retain recovery and cannot be replaced with false success toasts.
- **UX-02-06** Mobile header neither overlaps content nor hides the sign-out or navigation action.

### UX-03 — Dark responsive sidebar and navigation information architecture

**Priority:** P0 · **Dependencies:** UX-01, UX-02 · **Files:** `core/workspace-navigation.ts`, `navigation.ts`, `app.scss`, existing navigation dialog.

**Work**

- Implement dark sidebar surface with grouped navigation, active row, keyboard focus, accessible label and optional collapse/expand on desktop.
- Preserve firm-level groups (Administration, Practice, Accounting, Audit) and their exact existing Angular Router destinations.
- Add contextual engagement navigation only when an engagement is known, within actor scope, and target routes already exist. Do not invent links or expose a whole firm's client list to an engagement-scoped user.
- Use the prototype's five-folder engagement navigation as a **visual metaphor or actual authorized document links**, not as hardcoded fake counts or inferred SharePoint provisioning success.
- Keep the established compact-screen Material dialog/navigation mechanism, escape/focus restoration, `routerLinkActive`, session-change closing and access-denied behavior.
- Sidebar collapse must not store sensitive user/context data in browser storage. Cosmetic preference may use a safe in-memory choice unless the existing project provides an approved preference mechanism.
- Avoid duplicating links between global sidebar and phase-specific workbench menus.

**Acceptance criteria**

- **UX-03-01** Desktop sidebar presents dark reference styling, active location and selected group clearly.
- **UX-03-02** All preexisting reachable staff/portal routes remain reachable and deep-link reload works.
- **UX-03-03** No unauthorized routes or protected counts appear due to copied prototype configuration.
- **UX-03-04** Desktop collapse, keyboard focus and mobile navigation open/close are reliable.
- **UX-03-05** On staff-to-client or client-to-staff identity changes, stale menus and engagement names disappear.
- **UX-03-06** Existing `app-navigation.spec.ts`, `core/navigation.spec.ts` and control compatibility assertions pass without being weakened.

### UX-04 — Real engagement selector and contextual navigation

**Priority:** P1 · **Dependencies:** UX-02, UX-03 · **Target:** header/sidebar, existing portfolio/engagement queries.

**Work**

- Give users a compact engagement selector like the reference, but populate it using an **existing authorized, bounded/paged query** wherever possible.
- Display client name, audit period and engagement service route only when the actor has access. Do not eagerly load an entire firm's engagements into global state.
- Make the route and existing Angular router the selected-context source. Switching opens the correct authorized engagement route and refreshes contextual ribbon; it does not call an approval/write command.
- A client-role portal cannot use the staff selector to enumerate sibling clients; hide or restrict it.
- Show scoped loading, empty, denied and stale-selection states. On an unknown or inaccessible engagement, clear previous context before rendering new content.
- If the existing backend query is insufficient, document the precise missing read contract first; implement a minimal named Application query only after explicit functional-scope review.

**Acceptance criteria**

- **UX-04-01** A staff user can switch between exactly those engagements returned as authorized.
- **UX-04-02** The current engagement label, breadcrumb and phase ribbon match the actual route ID.
- **UX-04-03** Cross-client/engagement guessed IDs never expose cached information from the prior selection.
- **UX-04-04** Selection survives refresh by URL, not by an unsafe client-side business-state store.
- **UX-04-05** No new duplicate engagement list or arbitrary unbounded background polling is introduced.

### UX-05 — Prototype-style seven-phase ribbon and actionable engagement workbench

**Priority:** P0/P1 · **Dependencies:** UX-01–UX-04 · **Files:** `engagement-workflow.{ts,html,scss}`, engagement container, possibly a small shared presentational component.

**Work**

- Adapt the reference's horizontal seven-phase ribbon with clear current, complete, upcoming, blocked, waiting, under-review and rework states.
- Reuse the existing `/api/ui/engagements/{id}/workflow` projection and existing checklist query. **Do not derive a new phase from UI booleans or manual task counts.**
- Show the ribbon only in valid engagement context (desktop under header or within engagement sublayout), not for unauthenticated/setup/public users. Avoid duplicating the same long ribbon inside content and global shell.
- Clicking a phase opens the relevant *authorized route/section*, not a state transition. Show `disabled` or a read-only look when a linked action is unavailable; do not misrepresent a locked stage as an approved stage.
- Under the ribbon display the current phase's role, next permitted action(s), due date when the API provides one, blockers and checklists. Do not invent times or owners from prototype fixtures.
- Preserve task percentage separately from professional gate readiness. Even `100%` task completion must not unlock a Partner opinion without the authoritative gate.
- Use 7 displayed phases mapping the existing 11 states, with no duplicate state table:

| Display phase | Canonical stages |
|---|---|
| Client Onboarding | `LEAD_INGESTION`, `PROPOSAL_GENERATION`, `DUAL_KEY_PENDING`, `ADVANCE_BILLING` |
| Audit Planning | `PORTAL_ACTIVE_PLANNING` |
| Audit Fieldwork | `FIELDWORK_EXECUTION` |
| Manager Review | `MANAGERIAL_REVIEW` |
| Partner Sign-off | `PARTNER_APPROVAL` |
| Report & Delivery | `DELIVERABLE_RELEASE` |
| Archive & Close | `COMPLIANCE_COUNTDOWN`, `ARCHIVED_READ_ONLY` |

**Acceptance criteria**

- **UX-05-01** All eleven canonical states map to exactly one of the seven display phases.
- **UX-05-02** Status, blocker text and permitted actions come from current authorized server records.
- **UX-05-03** A task-complete but approval-blocked scenario displays the blocker and never changes professional state.
- **UX-05-04** Phase clicks navigate/read only; no synthetic `COMPLETE`/`APPROVED` writes are made.
- **UX-05-05** Ribbon scrolls/wraps appropriately at 320/390/768/1440 widths; no inaccessible off-screen steps.
- **UX-05-06** `aria-current`, labels and screen-reader progress remain understandable without color.
- **UX-05-07** An engagement source or role change clears stale ribbon data immediately.

### UX-06 — Onboarding and planning: prototype cards, real decisions

**Priority:** P1 · **Dependencies:** UX-01, UX-05 · **Targets:** commercial, assessment/activation, resource scheduling, TB and materiality pages.

**Work**

- Bring the prototype's side-by-side **Key 1: Client Commercial Acceptance** and **Key 2: Partner Risk Clearance** into existing Angular onboarding/readiness displays where authorized.
- Show the separate **50% advance payment** card, explicit pending/paid allocation and exact blocker; do not combine or bypass the three independent gates.
- Preserve proposal/quotation revision identity, evidence and stale acceptance messaging; do not implement prototype `toggleCommercialKey()`/`togglePartnerKYC()` state toggles.
- Use the prototype's role/assignment cards, milestone widgets, five-folder visual structure and materiality layout as references; retain current Partner-authorized engine and human benchmark decisions.
- Unavailable SharePoint provisioning must say *pending/unverified*, even when the UI knows the expected five folder names.
- Preserve approval revision fencing, whole-advance requirement, finance identity and fields.

**Acceptance criteria**

- **UX-06-01** Both keys independently show current, stale, rejected and missing states where applicable.
- **UX-06-02** A full advance invoice with only partial receipt still shows activation blocked.
- **UX-06-03** Figma-like planning cards reproduce visual hierarchy but not synthetic numbers or risk bands.
- **UX-06-04** Current materiality, source mapping and planning approval keep existing integrity checks.
- **UX-06-05** Viewer role cannot press privileged actions even if prototype displays them.
- **UX-06-06** Form validation, lost-response recovery and relevant feature specs remain intact.

### UX-07 — FSLI split dashboard, source drill-down and fieldwork execution

**Priority:** P1 · **Dependencies:** UX-01, UX-05 · **Targets:** `engagements/statements.*`, `audit/fieldwork.*`, workpaper editor.

**Work**

- Recreate the reference's clear **upper P&L / lower balance sheet** dashboard, with accessible headers, current year, prior year, variance, risk band, assigned preparer/reviewer, review state and actions.
- **Reuse the existing real split dashboard and the approved statement basis**; do not reconstruct totals by summing visually loaded page rows.
- Provide visually consistent `Analytical Review` and `Audit Workprogram` row actions, using existing links and preserving exact line ID, mapping generation and review permissions.
- Keep row-level drill-downs to accepted TB/GL contributions, workpaper revisions, physical binder references and evidence links.
- Use responsive horizontal-scroll financial tables, sticky column labels where safe, and preserved table totals. No hidden/truncated precision.
- Re-style risk/status chips using centralized tokens; color must not be the only risk signal.
- Respect concurrent work: visual drawers/dialogs must not turn revisioned independent edits into shared mutable state.

**Acceptance criteria**

- **UX-07-01** P&L appears above B/S in split mode and both retain real approved-source figures.
- **UX-07-02** Current/prior numbers, negative signs, percentages and zero-denominator labels are unchanged by restyling.
- **UX-07-03** Row actions open the exact permitted FSLI workbench, never the wrong account or engagement.
- **UX-07-04** Source/mapping changes invalidate stale drill-downs and show the existing stale-basis state.
- **UX-07-05** Dense tables remain usable by keyboard, at 200% zoom and on narrow screens.
- **UX-07-06** Submission, review, rework and source evidence tests remain unchanged in outcome.

### UX-08 — Manager review, confirmations and Partner sign-off presentation

**Priority:** P1 · **Dependencies:** UX-01, UX-05–UX-07 · **Targets:** audit fieldwork, confirmations, review and completion pages.

**Work**

- Present an audit review overview with active review notes, returned items, required decisions, confirmation states, significant differences, SRM readiness and latest source identity.
- Adopt reference cards/tabs and action hierarchy for review notes, holding letters, sampling/review details and high-risk area inspection.
- Preserve exact review-note reason, independent reviewer constraints, latest-effective decision rules, pending-rework sources and gated outcomes.
- For Partner: present professional opinion options and basis only through the existing Partner-authorized command/UI. Make selected opinion, reason, exact candidate/document identity and signing status explicit.
- Do not enable “Approve”, “Sign”, “Issue” or “Clear confirmation” from display progress alone.
- Read, do not overwrite, signed/certificate and seal status. If signing infrastructure is not configured, display the real blocker.

**Acceptance criteria**

- **UX-08-01** Every review and sign-off control uses the existing authorized command.
- **UX-08-02** Returned results and unresolved notes remain visible as blocking/rework states.
- **UX-08-03** Critical pending confirmation visibly blocks final reporting actions.
- **UX-08-04** Reviewers cannot self-review contrary to current role and evidence restrictions.
- **UX-08-05** Only permitted Partner actors can complete Partner-only decisions.
- **UX-08-06** Changing a source/candidate invalidates stale display eligibility, with appropriate re-review prompts.

### UX-09 — Report, deliverables and archive UX

**Priority:** P1 · **Dependencies:** UX-01, UX-05, UX-08 · **Targets:** accounting package/presentation, completion/release and records screens.

**Work**

- Display the five-part deliverable bundle in structured cards, as in the prototype, but backed by real available document identities, signed-status metadata and release eligibility.
- Show current reviewed/candidate composition PDF/DOCX/XLSX and required management/accounting/Partner decisions **as returned by the backend**, with no visual-only promotion.
- Use clear controls for download/preview, hashes or safe shortened IDs, and exact-versus-stale artifact status.
- Show final balance invoice/delivery linkage using firm finance records. Do not silently issue an invoice just to populate a visual card.
- Redesign archive status with signature-based due date, local freeze vs external provider protection, and read-only view/download after genuine freeze.
- Treat an elapsed timer, pending durable operation or provider configuration as **not yet archived**.

**Acceptance criteria**

- **UX-09-01** The five deliverable groups are recognizable and linked to existing real records.
- **UX-09-02** No unsigned/draft artifact is presented as final or released.
- **UX-09-03** Reviewed exact document composition is visibly distinct from superseded, stale or missing versions.
- **UX-09-04** Archive state only indicates locked when the authorized backend local lock has actually committed.
- **UX-09-05** Download links remain client/engagement-scoped and nonpublic.
- **UX-09-06** Changes do not alter the existing signing, representation letter, release checkpoint, fee or retention gates.

### UX-10 — Restricted client portal visual parity

**Priority:** P1 · **Dependencies:** UX-01–UX-03 · **Targets:** `features/portal/*`.

**Work**

- Reproduce the clean portal's request lists, upload panels, document status cards, finance/download links and responsive layout without copying staff-only shell or controls.
- Use a portal-specific navigation variant drawing only on `SessionService` and authorized portal endpoints; no `role` selector or selectable foreign client.
- Preserve first sign-in requirements, exact upload requests, SHA-256 receipts, duplicate handling, retry reconciliation, file-size/type errors and request-level clarified/rejected states.
- Represent upload outcomes precisely: pending, uploading, submitted, under review, clarification required, accepted, frozen; do not claim provider delivery until verified.
- Respect portal freeze after signed release and recorded archiving restrictions.

**Acceptance criteria**

- **UX-10-01** Client users cannot see staff workpaper/AML/financial allocation evidence or firm-wide menu items.
- **UX-10-02** Wrong-client and guessed document links are refused by the server and render no stale content.
- **UX-10-03** Upload failures and uncertainty still provide safe retry/reconciliation instructions.
- **UX-10-04** A frozen engagement has no active upload control; permitted read-only documents remain accessible.
- **UX-10-05** Portal works fully by keyboard and mobile touch and clearly labels each real status.

### UX-11 — Practice management, task queues, templates and firm finance

**Priority:** P1/P2 · **Dependencies:** UX-01–UX-05 · **Targets:** `features/practice/*`, `features/finance/*`, `features/portfolio/*`.

**Work**

- Adopt reference compact overview cards, filter toolbars, queue board, due-date calendar, timesheets, rate-card summaries and profit/ledger tables.
- **Reuse existing** My Work, Kanban/calendar, versioned workflow templates, resource allocation, time and firm ledger screens. Do not create a second task/template engine.
- Ensure responsive tables preserve QAR precision, signed ledger sides, invoice/payment status and existing page boundaries.
- Preserve distinction between internal firm books and client financial statements.
- Keep task action eligibility `can*` values and current revision; no static `Mark Complete` action built from prototype fixtures.
- Do not introduce unbounded calendar/portfolio queries, fake utilization figures or computed frontend financial totals.
- Keep workflow templates version-pinned; editing a source template does not silently rewrite an active engagement's tasks.

**Acceptance criteria**

- **UX-11-01** Portfolio and queue screens reflect actual current authorized records.
- **UX-11-02** Board/calendar/list views show the same underlying task IDs/statuses and preserve queue filters.
- **UX-11-03** Time/finance amounts keep exact decimal-string transport and existing accounting semantics.
- **UX-11-04** Firm books cannot be confused with client accounting or consolidation.
- **UX-11-05** Template publication, task edits, review, and rework preserve revision and role fencing.
- **UX-11-06** Existing complex screens remain usable at desktop and mobile widths without hiding blocking warnings.

### UX-12 — Dialog, preview, notification and feedback consistency

**Priority:** P2 · **Dependencies:** UX-01–UX-03 and affected page slices.

**Work**

- Restyle existing Angular Material dialogs used for review notes, receipt viewing, holding letters, export/inspection, timesheets and document previews to echo the reference.
- Do not copy React modal implementations or create new record-generation commands unless separately scoped.
- Standardize confirm/cancel/close placement, focused title, submission indicators, error/validation messages and waiting-for-provider states.
- Continue using existing command outcome/receipt reconciliation; a toast is visual feedback, not evidence of committed work.
- Ensure long evidence names, real QAR amounts, multiline explanatory text, upload constraints, scroll and keyboard navigation work without clipping.

**Acceptance criteria**

- **UX-12-01** Open dialog receives expected initial focus; Escape handling respects irreversible/confidential review rules.
- **UX-12-02** Closing restores focus to the originating control, unless navigation or invalidation requires a different safe destination.
- **UX-12-03** UI errors and unknown outcomes remain visible and actionable.
- **UX-12-04** No protected command becomes one-click without existing explicit assent/confirmation.
- **UX-12-05** Modal size and scrolling work at 320px and 200% zoom.

### UX-13 — Accessibility, performance and responsive hardening

**Priority:** P0 for every slice; completion sweep after UX-12.

**Work**

- Target **WCAG 2.2 AA** for changed screens: semantics, labels, name/role/value, focus, error association, contrast and non-text contrast, color-independent statuses, zoom, reduced motion and keyboard-only navigation.
- Test widths 320, 390, 768, 1024 and 1440 pixels and 200% browser zoom; no inaccessible controls or unexplained horizontal page overflow. Financial tables may scroll inside clearly labeled regions.
- Preserve table paging/virtualization/query bounds; do not fetch every client engagement for the engagement switcher.
- Avoid layout shift on open dialogs, sticky header/ribbon overlap and nested inaccessible scroll traps.
- Reuse existing Angular dynamic imports, `@defer` and route splitting where present. Do not create a giant eager-loaded shell containing all workspaces.
- Never log private financial, audit, or session contents during screenshot testing.

**Acceptance criteria**

- **UX-13-01** All redesigned screens support mouse, keyboard and reasonable touch target sizes.
- **UX-13-02** All required context/actions are usable at specified viewports/zoom.
- **UX-13-03** No flashing/high-motion behavior is forced when reduced motion is requested.
- **UX-13-04** Contrast and form/error accessibility audit has recorded evidence and tracked exceptions.
- **UX-13-05** Responsive conversion does not remove backend blockers, permitted actions or tabular totals.
- **UX-13-06** Loading latency, bundle size and API request count are measured against pre-change baseline; material regressions are addressed before release.

### UX-14 — Automated UI/visual parity and end-to-end nonregression

**Priority:** P0 for final acceptance; incrementally with every slice.

**Work**

- Use existing Angular `*.spec.ts`, API route/spec contracts, Playwright .NET E2E and `app-navigation.spec.ts` / `control-compatibility.spec.ts`.
- Add screenshot captures for the approved reference and actual Angular screens under controlled viewport/data/font conditions. Review differences manually or with a consistent image-diff workflow; do not claim arbitrary pixel-percentage parity without stable baselines and approved tolerances.
- Add critical role/scenario journey checks, including staff, manager, Partner, client, denied-scope and offline/loading/error states.
- Preserve screenshots/outputs as controlled test artifacts; review for data sensitivity before any publication.
- Do not rewrite acceptance tests to assume mocked workflow states.

**Minimum end-to-end journeys**

1. Login → scoped portfolio → select engagement → navigate seven-phase ribbon → see correct server-derived blocker.
2. Onboarding → one commercial key missing → engagement letter blocked; both keys present but 50% advance partial → activation blocked.
3. Planning → approved mapping/materiality visible; stale mapping or materiality reflected after refresh.
4. Statements → split P&L/B/S → open FSLI analytical review/workpaper from the correct row; preserve precise QAR values and stale-basis handling.
5. Preparer result → independent Manager review → return with notes → rework → reviewed: show correct visual state and no bypass.
6. Critical confirmation unreturned → Partner/report issue blocked with reason; do not show success.
7. Package draft → candidate/composition → exact management/accounting/Partner workflow **where those underlying capabilities are currently accepted**; visually surface any missing readiness instead of faking completion.
8. Signed report/release → exact authorized downloads; archival timer ≠ frozen archive; actual frozen state remains read-only.
9. Client portal → permitted PBC upload and download → wrong-client denial → revoked role/session clears protected content.
10. My Work list ↔ board ↔ calendar and firm time/ledger pages keep current task IDs/amounts.
11. Mobile navigation keyboard/focus, engagement switcher, dialog and long financial table.
12. Reload every changed deep link and recover known/unknown command outcomes without duplicate writes.

**Acceptance criteria**

- **UX-14-01** Every modified route has behavioral component/contract coverage appropriate to its original assertions.
- **UX-14-02** A screenshot matrix covers every mapped reference screen with a documented variance decision.
- **UX-14-03** Staff/client role and cross-engagement denial regressions pass.
- **UX-14-04** Financial amount/sign/precision regressions and existing business-state checks pass.
- **UX-14-05** Key 11-to-7-state mapping, approval-gate and archive/freeze tests pass.
- **UX-14-06** Angular production build and full frontend specs pass; relevant targeted API/E2E tests pass.
- **UX-14-07** No test has been deleted, diluted or replaced with a screenshot-only assertion.

### UX-15 — Final scope inventory, rollout and owner acceptance

**Priority:** P0 completion gate · **Dependencies:** UX-00–UX-14.

**Work**

- Re-audit all production routes: global shell consistency for all; screen-level parity for designated core screens; safe exceptions for auth/setup, specialized high-density tools, and portal isolation.
- Verify visual quality with real role-specific test identities and approved synthetic data. Do not use prototype hardcoded credentials/roles or financial datasets.
- Verify route, E2E, migration/model-drift (if altered), API contract (if altered), accessibility and screenshot evidence on the **same exact candidate commit**.
- Obtain explicit owner approval for any intentional visual differences; record them, not hide them.
- Coordinate with current Angular migration readiness; **do not retire Blazor Web** or alter backend release gates as a side effect.
- Roll out only via existing approved release process; document fallback by reverting the presentation change rather than corrupting business data.

**Acceptance criteria**

- **UX-15-01** Every scoped production route is classified as implemented-and-verified, approved-exception, or open blocker; no unexplained omission.
- **UX-15-02** Actual browser screenshots of the candidate show the approved design across key roles, widths and interaction states.
- **UX-15-03** The owner approves visual differences and confirms usability for routine audit-firm work.
- **UX-15-04** Targeted and full required test evidence is green for the exact candidate or blocked with truthful disposition.
- **UX-15-05** Existing financial, audit, commercial, signing, session, role, provider, document and archive controls are unchanged or stronger.
- **UX-15-06** No new dependency or unapproved schema/business API write was introduced.
- **UX-15-07** Document status is updated truthfully, with links to actual PRs, tests and visual comparison evidence; no unsupported “100% production-ready” claim.

---

## 7. Engineering implementation guidance

### 7.1 Architectural placement

```
AuditSphereOps.Ui (Angular 22)
├── src/styles.scss                  existing global design tokens / Material theme
├── src/app/app.{html,scss,ts}        existing app header + responsive shell
├── src/app/navigation.ts            existing navigation catalogue
├── src/app/core/                    shared API, decoding, session, UI, dialogs
├── src/app/features/engagements/    existing 7-phase, lifecycle, statements
├── src/app/features/audit/          real fieldwork, reviews, completion
├── src/app/features/portal/         restricted client-only views
├── src/app/features/practice/       tasks, board, calendar, resources, time
└── src/app/features/finance/        firm books and billing

                   │ same-origin authorized reads/writes
                   ▼
AuditSphereOps.Api (/api/ui/*)
                   │ named Application commands/queries
                   ▼
AuditSphereOps.Application → Domain / Infrastructure / Worker
```

**Proposed shared UI components:** Add only where several feature pages truly benefit (for example, a small `audit-phase-ribbon`, `audit-context-switcher`, or `audit-data-card`). Avoid an unnecessary abstract component framework or a duplicate design system. Keep feature-specific markup in its existing feature file.

### 7.2 State, loading and authorization

- Angular signals/`api.resource` for reads, existing `CommandState` for authorized writes.
- Preserve the API edge decoder and exact-string financial values.
- Reuse existing `SessionService.invalidation()` and `core/drafts.ts`/`tab-drafts.ts`; do not persist client/engagement selection as mock business state in `localStorage`.
- For commands that may return an uncertain result, show pending/reconciliation state; never automatically repeat a write on visual navigation.
- Prefer router-encoded context and links over an extra global mutable store.
- Underlying `can*` fields and authoritative workflow projections decide action availability; styling decides only how it looks.

### 7.3 Route parity

- Keep existing full routes and route guards. Avoid broad renames just to match prototype screen labels.
- If a **new route is truly necessary**, update `app.routes.ts`, `UiEndpoints.Infrastructure.cs` `SpaRoutes`, guard, contract test and deep-link integration together.
- Context menus may navigate to existing destinations. Never expose a “working” button with an inert click handler.
- Keep the client portal separate from staff navigation and clearance details.

### 7.4 Performance and source constraints

- Do not run multiple independent requests for identical engagement phase data simply because a ribbon appears in both shell and page.
- Do not duplicate source queries or calculate totals in presentation when the current API already supplies them.
- Preserve existing pagination, per-page bounded result limits and lazy-loaded routes.
- Avoid new public image/CDN/font dependencies unless reviewed. Don't copy sensitive prototype data into screenshots or seed files.
- Make no schema changes unless a specific approved user-visible capability cannot be achieved through existing read contracts.

---

## 8. Work-package dependencies and phased delivery

| Slice | Packages | User-visible result | Required verification before next slice |
|---|---|---|---|
| **A — foundation** | UX-00, UX-01 | Approved reference/screenshots; shared SCSS/Material styling | Token/contrast, shared specs, no global regressions |
| **B — shell** | UX-02, UX-03 | Dark header and sidebar on authenticated Angular routes | Navigation, role, sign-out, mobile, deep-link E2E |
| **C — context/workflow** | UX-04, UX-05 | Real engagement selector and seven-phase ribbon | Scope, session, phase/gate regressions |
| **D — core audit screens** | UX-06, UX-07, UX-08 | Onboarding, planning, FSLI split and review/sign-off UX | Real scenarios, exact amounts, reviewer/Partner denials |
| **E — close & firm practice** | UX-09, UX-10, UX-11 | Deliverables, archive, client portal and practice boards/ledger | PBC, finance, release, archive, task regressions |
| **F — refinement & sign-off** | UX-12, UX-13, UX-14, UX-15 | Consistent dialogs, responsive parity, approved screenshots | Complete release verification, human visual review |

**Delivery rule:** Split large slice D/E further by actual feature routes. Every implementation pull request should have a narrow intent, visible UI before/after evidence, relevant tests and no unrelated service rewrites. A future slice must not require merging unreviewed or unapproved previous slices.

### Preferred pull-request boundaries

- PR1: tokens + shared visuals (no business changes).
- PR2: shell/header/sidebar/mobile (no API changes unless proven necessary).
- PR3: engagement selector + server-driven ribbon.
- PR4+: grouped by existing features (commercial/planning; statements/fieldwork; review/completion; portal; practice/finance).
- Final PR(s): accessibility, visual snapshots, docs/inventory and parity recheck.

PR merges require the **owner's explicit authorization** under the established repo workflow. Do not merge automatically based on this story.

---

## 9. Required test plan and verification commands

Use the repository's pinned toolchain and instructions in `AGENTS.md`; these examples are **commands to run when implementing**, not evidence that they passed while this story was drafted.

```bash
# From AuditSphere repository root.
npm --prefix src/AuditSphereOps.Ui ci
npm --prefix src/AuditSphereOps.Ui run build
npm --prefix src/AuditSphereOps.Ui run test:ci

# Run if backend/route contracts are touched and at final release verification.
dotnet tool restore
dotnet restore AuditSphereOps.slnx --locked-mode
dotnet build AuditSphereOps.slnx --no-restore --configuration Release --maxcpucount:1
dotnet test AuditSphereOps.slnx --no-build --configuration Release --maxcpucount:1

# Additional repository-enforced checks, if relevant to changed files.
python3 scripts/ui/inventory.py --check
bash scripts/contracts/verify-openapi.sh
python3 scripts/docs/validate-markdown-documentation.py
python3 scripts/docs/validate-markdown-filenames.py
python3 scripts/docs/validate-narrative-metrics.py
```

Run PostgreSQL/API/browser integration tests through the existing supported test profile; do not invent DB credentials or connect to production for screenshot/testing purposes. If a command cannot be run, record the precise blocker. **Tests must be observed on the candidate commit**, not assumed from an earlier run.

### Visual screenshot review matrix

| Reference view | Baseline state | Mandatory comparisons |
|---|---|---|
| Header/sidebar/ribbon | Normal engagement, blocked engagement | Desktop, mobile, keyboard focus |
| Onboarding | Only commercial approved, both approved, advance unpaid | Key separation, blockers, cards |
| Planning | Assigned team, pending materiality | Tables, milestones, responsible roles |
| Fieldwork | Split FSLI + one opened row | Numbers, actions, table scroll |
| Manager review | Returned and reviewed cases | Rework reasons, status visibility |
| Partner | Opinion blocked and eligible | Decision authority, prompts |
| Delivery | Draft vs candidate vs released | Document identity, five-part cards |
| Archive | Countdown vs actual frozen | Truthful lock/provider labels |
| Client portal | Request open vs upload blocked | Isolation, mobile input controls |
| Practice/finance | Work board, calendar, firm ledger | Density, QAR tabular columns |
| Dialogs | Review note, file preview, receipt | Focus, scroll, errors |

**Screenshot test caveat:** The React prototype uses mock data while Angular uses real authorized data. Comparing raw pixels only makes sense after matching *structural content* and identical synthetic fixtures. Otherwise use component geometry, spacing, typography, layout hierarchy and approved semantic differences. No invented universal "98% pixel parity" score.

---

## 10. Special negative scenarios that MUST be tested

| Scenario | Required outcome |
|---|---|
| User attempts to click the “Partner approved” looking phase | Navigates/reads only; no approval recorded |
| Client sees engagement switcher | No listing of unrelated staff/client engagements |
| User switches from Client A to Client B | Client A's name, financials and navigation clear immediately |
| Revoked session while dialog is open | Protected dialog closes or safely invalidates; stale data hidden |
| Task completion reaches 100%, management approval absent | Ribbon still displays blocker; issue/sign controls remain blocked |
| Advance invoice exists, advance partially allocated | Planning activation still blocked |
| Different reviewer attempts to approve own submitted procedure | Underlying command refuses, with truthful UI feedback |
| Report candidate changed after displayed approval | UI refreshes, stale approval cannot visually or operationally authorize release |
| Critical confirmation outstanding | Review/report release controls remain blocked |
| Archive countdown reaches zero but freeze not committed | State remains due/pending, not “Permanently archived” |
| Provider upload status uncertain | Explicit pending/reconcile state; no false success toast |
| Financial row contains negative QAR or six-decimal value | Correct sign/precision survives formatting and scrolling |
| Slow API request resolves after route/context switch | Old response cannot overwrite new user's/engagement's view |
| Narrow screen and 200% zoom | All mandatory actions and blockers remain reachable |
| Dialog canceled/closed after unsaved review | No change or false reviewed state persisted |
| Legacy/deep-linked route reloaded directly | Correct guarded route loads or clearly denies access |

---

## 11. Definition of Done — measurable exit contract

A story package may be marked `IMPLEMENTED_LOCAL` only after its full targeted criteria, relevant tests, screenshots and review are complete. **A published UI release is not automatically proof of firm-wide product readiness.**

### 11.1 Functionally correct

- All changed routes continue to use current role/session/source-gated services.
- The seven-phase ribbon is only a projection of the existing eleven-state lifecycle.
- All impacted commercial, accounting, audit, professional review, document, signing, release, firm finance and archive actions preserve their existing guards.
- Draft, stale, blocked, pending and unknown outcomes are accurately represented.
- The fake mock-data and role toggles from `auditspherefigma` were not brought into production.

### 11.2 Visually accepted

- A reviewer signs off a screen-level mapping comparing the prototype to Angular at approved breakpoints.
- Header, sidebar, seven-phase ribbon, cards, typography, tables, actions and status components follow the approved design direction.
- Every core prototype workbench has a functional Angular counterpart or an explicitly approved difference.
- Real data density and long entity names do not break layout.

### 11.3 Technically verified

- Angular build and `test:ci` green on exact candidate.
- Existing route-navigation, control and impacted feature specs green without weakened assertions.
- Required PostgreSQL/API/E2E checks green, or honestly blocked with reasons.
- No unauthorized financial arithmetic, backend state mutation, security leakage or cross-client data exposure.
- Accessibility, responsive screen, performance and visual screenshot evidence reviewed.
- No unapproved packages/fonts/assets introduced, and any modified API route catalogue remains synchronized.
- Source and test outcomes recorded in the correct execution ledger using observed facts only.

### 11.4 Release/operational boundary

- Deployment and protected-merge authorization is separate from this design request.
- Blazor reference/rollback retirement remains out of scope and cannot be inferred.
- Live identity/SharePoint provider acceptance, firm certificate policy and full practice-management product readiness require their separate gates.

---

## 12. Developer handoff checklist

Before starting each PR, the agent should be able to state:

- [ ] Which exact Figma prototype component is being adapted?
- [ ] Which exact Angular route and files already implement the real capability?
- [ ] What is the **visual-only** delta and what is intentionally unchanged?
- [ ] Which user role and server-authorized records provide the displayed state?
- [ ] Which route deep links, source IDs, approvals and drafts may be affected?
- [ ] Which smallest tests prove the new layout and unaffected workflow behavior?
- [ ] What screenshot before/after proves the requested visual change?
- [ ] Are any fonts/assets/dependencies used, and was owner approval obtained?
- [ ] What is the rollback path if the visual change causes navigation regressions?

At completion, report the exact commit, changed paths, implemented criteria, test commands/results, screenshots, approved exceptions and open blockers. Do not describe partial/unverified work as complete.

---

## 13. Copy/paste coding-agent implementation prompt

> **TASK:** Implement **AS-UX-001 — AuditSphere Figma-Style UI/UX Parity** from this document, incrementally and with rigorous non-regression.
>
> **Source of visual design:** `https://github.com/nirzaf/auditspherefigma` at pinned reference commit `c1202ef8751e3465d380735bf76b27085cb38bd6`. This is a Next.js/React/Tailwind prototype and contains mock state. Treat its components and visual appearance as design references only.
>
> **Implementation repository:** `https://github.com/nirzaf/AuditSphere` (`master` baseline `b96c8f8cc23a10c54ff63301132c9b0a11c15231`; recheck HEAD before work). This is Angular 22/Material/CDK/SCSS with ASP.NET Core API. Keep the existing stack, routes, domain services, PostgreSQL, approved ADRs and production controls.
>
> **First action — analysis only:** Read `AGENTS.md`, `docs/architecture/auditsphere-angular-conventions-current.md`, the current architecture code map, route catalogue, current task board and only the relevant files in this story. Inspect both repositories' latest content. Report the exact UX gaps and the smallest coherent PR slice. Do not implement before clearly identifying scope, non-goals, acceptance criteria and existing tests.
>
> **Implementation objective:** Deliver the `auditspherefigma` look and experience in the production Angular application: dark header/sidebar, clear engagement selector, horizontal seven-phase progress ribbon, professional light canvas, consistent cards/tables/statuses, compact FSLI dashboard, coherent client portal, practice work queues, review/Partner screens, deliverables and archive. Maintain a consistent design token system and accessible responsive navigation.
>
> **Non-negotiable:** Do not add React, Next.js, Tailwind, Lucide React, mock React store or simulated roles to production. Do not introduce an alternative state machine, financial calculator, approval mechanism, provider integration or protected command. Never copy `INITIAL_ENGAGEMENTS` or localStorage business state from the prototype. Never infer an approval/payment/release/archive from a visual phase. Existing server-authorized `can*` flags, roles, document identities, source revisions, 50/50 advance gate, Partner authority and immutable history remain authoritative. Preserve exact QAR decimal-string behavior, client/firm accounting boundaries and scoped session invalidation. Keep Blazor rollback/reference until its independent retirement gate is approved.
>
> **Order:** Implement UX-00/01 baseline/tokens first; UX-02/03 header/sidebar second; UX-04/05 engagement context/ribbon third; then adapt existing Angular screens in the order UX-06 through UX-12; validate UX-13/14 continuously and perform UX-15 sign-off at the end. Each PR should change only files directly needed for its slice. Avoid a wholesale application rewrite and avoid unnecessary architecture abstractions.
>
> **Testing:** Run the existing Angular build/specs and relevant navigation/control tests after each slice. Preserve or strengthen existing API/E2E tests for touched commands; at final candidate run the complete required verification on the same commit. Capture before/after screenshots with approved synthetic data at 1440, 768, 390 and 320px; verify keyboard/zoom/contrast/reduced-motion. Test denied scope, context switching, pending errors, money precision and gates. Do not weaken tests to make the redesign pass.
>
> **Completion response:** Provide a per-story acceptance table, actual file diffs, screenshot comparison links/artifacts, commands executed with pass/fail counts, remaining unverified cases, and a statement distinguishing UI parity from functional and production readiness. Do not mark 100% complete until every applicable criterion has evidence.
>
> **Git workflow:** Do not merge PRs or perform irreversible repository/configuration changes without the owner's workflow-required authorization. Work on an appropriate feature branch and request review; do not change branch protections or professional approvals to shortcut completion.

---

## 14. Final implementation judgment

**Recommended:** Adopt the **look-and-feel** of `auditspherefigma` through incremental Angular SCSS/Material and component changes, preserving AuditSphere's real backend workflows and controls. The highest-value visible results are the dark shell, reliable engagement context, and server-derived seven-phase ribbon. The highest-risk failures to prevent are copying mock role/state logic, breaking client isolation, hiding mandatory blockers, and changing financial/audit sign-off semantics.

**This document is an implementation plan, not an implementation report.** Source files were reviewed at the pinned revisions; neither application was rendered or screenshot-compared during authorship, and no repository code, database state, approvals or deployments were modified.
