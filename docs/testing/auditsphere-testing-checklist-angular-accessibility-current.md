# AuditSphere Angular Rendered Accessibility Checklist

**Status:** CURRENT

**Purpose:** Repeatable, scoped rendered accessibility acceptance for changed Angular workflows and the shared shell.
**Audience:** AI coding agents, UI engineers and qualified accessibility reviewers.

This checklist defines how to gather evidence. It does not itself establish that a feature passed, that the whole product conforms to WCAG, or that a professional workflow is approved. Store volatile source SHAs, browser/AT versions, fixture identity, observations and open findings in `docs/execution/status.json`.

## Before testing

- [ ] Identify the exact UI source commit and confirm the browser is serving that source. Record the API source separately when it is not the same build.
- [ ] Use an explicitly authorized local/test environment and existing synthetic fixtures. Keep staff and client sessions separate.
- [ ] Record role, route, fixture, browser/version, viewport, zoom and reduced-motion setting. Do not include client secrets or personal records in screenshots or logs.
- [ ] Confirm the test fixture is suitable for the surface. An empty queue can verify its empty state; it cannot verify actions that require a record.
- [ ] Avoid creating or approving business records, changing permissions, performing professional sign-offs or contacting external providers merely to make a screen testable.

## Rendered workflow matrix

Record each row as `PASS`, `PARTIAL`, `FAIL`, `NOT_RUN` or `BLOCKED_EXTERNAL`, with a short observation and evidence reference.

| Surface | Keyboard and rendered flow | Acceptance observations |
|---|---|---|
| Shared staff/client shell | Open/close navigation, change route, resize, and observe session loss where the fixture supports it | Focus order, containment and return focus are logical; main content receives meaningful route focus; revocation removes protected content. Keep staff and client sessions distinct. |
| Journal editor | Edit a line, exercise invalid input, inspect preview, cancel, and use a test harness for an unknown response | Controls and errors have usable names/associations; keyboard use does not submit accidentally; preview/receipt state is announced; unknown outcomes retain recovery references. |
| Evidence action editor | Select an allowed result, edit rationale, review, cancel/discard | Selection context and control names are clear; fresh assent is required; dialog focus returns; stale dialog results cannot act. |
| Engagement planning | Inspect staffing and independent budget preparation/review; inspect budget table | Controls are named; disabled reasons do not rely on color; table headers/caption are understandable; focus survives updates. |
| Consolidation | Inspect pages, counterparty inputs, exact amounts, journal lines and review controls | Pagination and scroll regions are keyboard-operable; row/column relationships and amount units are clear; denied or unavailable actions cannot be reached as enabled. |
| Fieldwork | Change sections/tabs, inspect procedure review, evidence/notes and sampling controls | Tab behavior matches the component; procedure/evidence context is clear; status/error changes are perceivable; routine updates do not discard focus. |
| Empty/loading/denied/stale/invalid/unknown states | Inspect each state supported by the authorized fixture | State is understandable and operable; missing records are not reported as success; repeated announcements do not obscure work. |
| Responsive and motion | Check 200% and 400% zoom, narrow width, reduced motion and rendered contrast | Required content remains available; reflow works where applicable; genuinely two-dimensional tables have bounded scrolling; focus and non-color status cues remain visible. |

Do not submit financial, audit, access or approval commands as part of an accessibility smoke check unless the environment and exact synthetic mutation are explicitly authorized. Use existing test harnesses for simulated unknown outcomes.

### Reflow and keyboard focus

Use native browser zoom for the zoom checks; changing device scale alone is not equivalent. Record the resulting CSS viewport alongside the zoom percentage. At narrow widths, activate the skip link and Tab to the first content control: its focus indication and control must remain visible, including below a wrapped header. Check horizontal tables with the keyboard as well as the pointer. Restore browser zoom, viewport and media emulation after inspection.

### Reuse the existing synthetic browser handoffs

The existing owned E2E journeys can pause their isolated API/database for a built-in browser inspection. Select only the relevant journey and set `AUDITSPHERE_BROWSER_FIXTURE_TARGET`, plus unique temporary-file basenames in `AUDITSPHERE_BROWSER_FIXTURE_READY_FILE` and `AUDITSPHERE_BROWSER_FIXTURE_RELEASE_FILE`. The ready JSON supplies the actual origin, route and expiry; use normal synthetic sign-in on that origin. Inspect without confirming business commands, then create the release file so the journey can finish its own assertions and dispose its resources.

| Handoff target | Existing journey | Read-only inspection |
| --- | --- | --- |
| `planning-budget-accessibility` | `AngularBudgetPreparationJourneyTests.ExactReviewLostResponseReloadAndRetainedReceiptNeverDuplicateBudget` | Exact rates, review focus, cancellation and return focus. |
| `evidence-action-pre-dispatch` | `AngularEvidenceActionsJourneyTests.KeyboardReviewPreviewCancelAndFreshAssentPreserveFocusOwnership` | Allowed selection, rationale, review/cancel and fresh-assent controls. |
| `consolidation-intercompany` | `AngularConsolidationIntercompanyJourneyTests.RegularScopeRetainsExactOutsidePerimeterMatchForKeyboardReview` | Named pagers, retained amounts, scoped review controls and table scrolling. |
| `fieldwork-populated-accessibility` | `AngularAuditFieldworkParityJourneyTests.AdoptedProgramReviewedFieldworkAndFrozenWorkpaperSurviveReload` | Programme/procedure context, tabs, evidence and review controls. |

The handoff expires after 90 seconds. A closed server or cleared session after teardown is a fixture limit, not evidence of an application failure. Record only interactions completed before expiry. Different loopback ports share host cookies; use isolated browser contexts for simultaneous actors, or inspect actors sequentially through normal sign-in. Never treat separate ports as cookie isolation. Automated journey results and built-in observations remain separate evidence; neither replaces human assistive-technology review.

## Automated accessibility evidence (AUTOMATED_A11Y_ACCEPTANCE)

Agent-run Playwright (Chromium, headless) on 22 October 2026, UI source 6969a469 (unchanged by the test-only commit that records this), synthetic administrator fixture from `OwnedHost`, loopback API. This is automated evidence only; it is not a human assessor result and not a compliance certification.

| Check | Route/state | Test | Result |
|---|---|---|---|
| Landmarks, heading order, skip link reached by keyboard, route focus to main, live-region announcement | `/ui/app`, `/ui/app/practice/leads`, `/ui/app/administration/users` | `AngularAccessibilityDeepAcceptanceTests.ScreenReaderProxySemantics_LiveRegionsAndDialogMatrix_HoldAcrossTheShell` | PASS |
| Dialog focus entry, Tab/Shift+Tab containment (no trap outside dialog), Escape dismissal, return focus, honest `aria-expanded` | `/ui/app/administration/users` role dialog | same test | PASS |
| Every visible control named; every `aria-describedby` resolves; invalid controls reference error text; repeated row actions carry row context (209 controls, 0 findings; injected bad markup is flagged) | `/ui/app`, `/ui/app/practice/leads`, `/ui/app/administration/users`, `/ui/app/practice/commercial-settings` | `AngularAccessibilityDeepAcceptanceTests.RenderedControls_AreNamedDescribedAndDistinct_AndReducedMotionStopsAnimation` | PASS |
| Reduced motion collapses animation and transition durations to at most 1 ms | same routes with `prefers-reduced-motion: reduce` | same test | PASS |
| Reflow across 320, 390, 760, 1024, 1440 and 1920 px CSS widths | staff and detail routes | `AngularRouteAndShellMigrationSweepTests.StaffAndDetailRoutesReflowAcrossTheSixViewportMatrix` | PASS |
| Native browser zoom at 200% and 400% | — | not automatable with viewport emulation | NOT_RUN |

The rendered workflow matrix rows above still need attributable per-surface observations; automated results inform them but do not close them.

## Human assistive-technology review

- [ ] A knowledgeable reviewer uses a screen reader on the changed workflow and records the actual screen reader and browser versions.
- [ ] Record reading order, control names/roles/states, status/error announcements, dialog focus, and any observed problem with its route and control.
- [ ] Automated accessibility trees, source inspection and static lint can inform this review but do not replace it.

If no qualified reviewer or suitable fixture is available, keep the affected row `BLOCKED_EXTERNAL` or `PARTIAL`; do not infer a pass.

## Completion rule

The scoped acceptance is complete only when every applicable row has attributable rendered observations, the required human review is recorded, and no required finding remains open. `NOT_RUN`, `UNVERIFIED`, `FAIL`, `PARTIAL` and `BLOCKED_EXTERNAL` remain visible. This checklist is scoped workflow evidence, not a whole-product accessibility certification.
