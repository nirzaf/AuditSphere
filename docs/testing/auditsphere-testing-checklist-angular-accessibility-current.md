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

## Human assistive-technology review

- [ ] A knowledgeable reviewer uses a screen reader on the changed workflow and records the actual screen reader and browser versions.
- [ ] Record reading order, control names/roles/states, status/error announcements, dialog focus, and any observed problem with its route and control.
- [ ] Automated accessibility trees, source inspection and static lint can inform this review but do not replace it.

If no qualified reviewer or suitable fixture is available, keep the affected row `BLOCKED_EXTERNAL` or `PARTIAL`; do not infer a pass.

## Completion rule

The scoped acceptance is complete only when every applicable row has attributable rendered observations, the required human review is recorded, and no required finding remains open. `NOT_RUN`, `UNVERIFIED`, `FAIL`, `PARTIAL` and `BLOCKED_EXTERNAL` remain visible. This checklist is scoped workflow evidence, not a whole-product accessibility certification.
