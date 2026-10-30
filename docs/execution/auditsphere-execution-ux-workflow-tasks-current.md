# AuditSphere — UX workflow task register

**Status:** CURRENT execution register.

The owner requested implementation of pending tasks in the attached UX audit. The attachments are proposal data, not professional policy, role grants, activation approval or release evidence. Current requirements and approved ADRs govern each implementation. This register is independent of original requirements, Axelor, practice, Atomic, Claude, role and Technical Stories registers.

## Attached sources

- `AuditSphere_UX_Workflow_Atomic_Spec_c7c9543.md` — SHA-256 `50d0c157e838f759ec826d5fda5e58e25e058e608ad7de8117bf3cb8d563546c`.
- `AuditSphere_UX_Task_Index_c7c9543.json` — SHA-256 `4483549c7ca615ff0ad34896b86d349ae8d7bae636bc5534cf956d8459b39a73`.

The audit describes an earlier source snapshot. Each packet requires current-source tracing before engineering; audit observations alone do not prove a current defect. Exact source and verification identities belong in [status.json](status.json).

## Packet disposition

| Packet | Requested behavior | Dependencies | Current disposition |
| --- | --- | --- | --- |
| D00 | Decide the true cross-firm switching boundary | Independent | Independent identity-contract decision. Current-firm work may proceed; no cross-firm session or grant is inferred. |
| U01 | Make navigation and My Work sections capability- and availability-aware | Independent | Owner-projected availability and scoped recovery implemented; focused synthetic evidence recorded below. Full AC01–AC06, integrated role and release acceptance remain open. |
| U02 | Provide authorized context choices and repair the portfolio handoff | Independent | Pending current-source assessment and scoped implementation. |
| U03 | Add a persistent current-context header and safe client/engagement switcher | U01, U02 | Pending current-source assessment and scoped implementation. |
| U04 | Preserve and scope My Work filters in the route | U02 | Pending current-source assessment and scoped implementation. |
| U05 | Make pending financial-package review discovery complete and paged | Independent | Implemented; focused native/Angular and additive contract checks passed. Exact successor CI, populated browser paging and integrated role acceptance remain pending. |
| U06 | Remove false authority claims from the package-review preview | Independent | Implemented; focused regression and production build verified. Integrated browser/role acceptance remains pending. |
| U07 | Expose existing package-review work inside My Work | U01, U05, U06 | Pending current-source assessment and scoped implementation. |
| U08 | Surface existing engagement next actions in My Work | U01, U02 | Pending current-source assessment and scoped implementation. |
| U09 | Make Portfolio a concise triage page using existing projections | U01, U02, U04, U07, U08 | Pending current-source assessment and scoped implementation. |
| U10 | Reduce task-card clutter without hiding actionable facts | Independent | Implemented; focused rendering/recovery, responsive keyboard and final recovery-focus browser checks passed. Exact successor CI and integrated role acceptance remain pending. |
| U11 | Guide template preparation through unresolved inputs first | Independent | Pending current-source assessment and scoped implementation. |
| U12 | Attach authorized resolution guidance to lifecycle blockers | Independent | Pending current-source assessment and scoped implementation. |
| U13 | Distinguish idempotency conflicts from stale revisions in command guidance | Independent | Implemented; focused regression and production build verified. Integrated browser/role acceptance remains pending. |
| U14 | Make an exact linked task usable without locating its queue page | U01 | Pending current-source assessment and scoped implementation. |
| V01 | Qualify the changed journeys and record activation boundaries | U01, U02, U03, U04, U05, U06, U07, U08, U09, U10, U11, U12, U13, U14 | Pending integrated final-source CI, built-in browser journeys, responsive/keyboard checks and qualified activation evidence. |

## Implemented behavior

U01 adds an authenticated staff availability projection assembled from existing destination owners. My Work task, procedure-review and template sections have separate `available`, `not_authorized` and `not_enabled` states; the accounting review destination remains independently scoped. The shell and My Work suppress disallowed section reads and distinguish a disabled or unavailable feature from an authorized empty queue. A bounded scope-window limit is not presented as an authorization denial. These presentation hints do not authorize direct URLs, destination reads or commands, and add no role grant or feature activation.

The server bounds each hint to at most 30 seconds and to the earliest active timed role expiry, rejects expiry during projection construction and rechecks the actor. The client binds it to firm, user, session generation and local invalidation, deducts the measured request duration, uses monotonic and wall-clock deadlines, and fences dependent data and late responses when availability or session changes. Renewals do not extend an expired hint; a workspace-availability 401 clears the shared session. Revocation remains subject to the bounded refresh and the destination's own current authorization; the hint is not immediate revocation notification.

Pending task actions and evidence, ordering and applicability receipts use per-tab recovery references scoped to the current firm and user. Discovery is bounded to 500 storage entries and 1,000,000 characters, reports incomplete storage rather than a false empty result, and checks candidates in batches. Retained task details require a fresh exact authorized task or panel workspace read bound to the current session and availability revision. Recovery checks the original reference, preserves unresolved or unrecognized keys and does not automatically resend; an exact retry, where supported, remains an explicit reviewed action. Prior-session serialization-fence checking is generic, does not read or disclose a task, does not remove a task action record and does not establish an earlier outcome.

### U01 retained focused evidence

The following are recorded observations for `92dfb98fbd979cd8e6454d64f1232abfe73a0070` plus the respective uncommitted WIP at the time of each run, not a single immutable final-source CI result:

- The synthetic PostgreSQL owner/scope test `WorkspaceAvailability_UsesCurrentOwnersAndKeepsMixedScopesSeparate` reported 1 passed. Its fixtures cover Staff, Reviewer, AccountingReviewer and mixed engagement roles, disabled workflow, expiry/revocation and stale epoch, scoped group navigation and no oversight observation. Evidence: `.local/autonomous/evidence/ux-workflow-20261030/u01-native-matrix.log` and `.trx`.
- The eight-file affected Angular cohort reported 161 passed, followed by a three-file queue/registry/cancellation cohort reporting 134 passed; the separate navigation/display cohort reported 17 passed. Evidence: `u01-handoff-session-invalidation.log`, `u01-handoff-session-read-final.log` and `u01-navigation-display-final.log` in the same local evidence directory. These counts cover their selected files and recorded WIP, not every role or integrated journey.
- `u01-openapi-verify.log` reports the emitted API contract was current for that run. `u01-angular-build-current.log` records a successful Angular build with two SCSS budget warnings, for `commercial/settings.scss` and `app.scss`. These records are not current-commit CI evidence.
- `u01-browser-final-scoped-review.json` records a built-in browser observation using one synthetic staff account: team list, assignment choices opened and cancelled without mutation, an empty receipt scan, and mobile navigation at 390 × 844 with Escape and focus return. Console errors were empty. The record identifies base SHA, tracked-diff SHA-256 `43ce3ced7169026d3237eb65868d257529784c257c80215933d804fa4c7effcd` and individual untracked-file hashes; those identify the observed WIP, not necessarily every subsequent shared-checkout edit. It did not inspect network errors or exercise task commands, interrupted outcomes, actual revocation, a role journey, live tenants or professional/human assistive-technology acceptance. It also notes a narrow desktop task cell without attributing cause.

Package selection preview reports selected packages and directs users to current workspace actions. Partner guidance names the currently assigned Engagement Partner. Selection is not an eligibility decision; no decision is recorded by preview. Existing fresh-queue selection and session invalidation remain.

The earlier U13 command recovery change distinguishes request-identity conflicts from revision/generation changes. Unknown conflict codes receive neutral guidance. Existing validation messages, unknown-outcome handling, session fences and single-send behavior remain. No new key, automatic retry or professional authority was introduced.

## Using the changed queues

Use **Check older packages** while continuation is available, including when the current candidate window has no eligible rows. The displayed row count is page-local. Moving pages clears current selection; saved IDs can be restored only when present in the freshly authorized current page. Refresh restarts at the newest window. Open each package for current authorized review actions.

The task table preserves a readable task column while retaining horizontal keyboard scrolling. Task source details retains source/template/mapping identities. More task actions retains activity, ordering and authorized recovery/assignment controls. Opening a disclosure records no task decision. The visible blocker and eligible primary action remain outside secondary actions. Closing a recovery panel after changing display reveals the current task action before restoring focus.

## Remaining acceptance

Continue U01 role and scope verification, including direct and deep-link authorization, expiry/revocation/session races, feature-off and mixed-role destinations, recovery and full keyboard/mobile workflows on the exact final source. Integrated role journeys, exact-source CI, network inspection, qualified human accessibility review and professional/external acceptance remain open; the focused evidence above does not close U01 AC01–AC06 or V01. Continue dependent context, My Work, preparation and blocker-guidance packets independently where ready. Preserve authorization before paging, unavailable versus zero, command freshness, exact task-link identity and existing financial boundaries. D00 blocks only true cross-firm switching. No tenant feature switch is authorized by this register.
