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
| U01 | Make navigation and My Work sections capability- and availability-aware | Independent | Pending current-source assessment and scoped implementation. |
| U02 | Provide authorized context choices and repair the portfolio handoff | Independent | Pending current-source assessment and scoped implementation. |
| U03 | Add a persistent current-context header and safe client/engagement switcher | U01, U02 | Pending current-source assessment and scoped implementation. |
| U04 | Preserve and scope My Work filters in the route | U02 | Pending current-source assessment and scoped implementation. |
| U05 | Make pending financial-package review discovery complete and paged | Independent | Pending current-source assessment and scoped implementation. |
| U06 | Remove false authority claims from the package-review preview | Independent | Implemented; focused regression and production build verified. Integrated browser/role acceptance remains pending. |
| U07 | Expose existing package-review work inside My Work | U01, U05, U06 | Pending current-source assessment and scoped implementation. |
| U08 | Surface existing engagement next actions in My Work | U01, U02 | Pending current-source assessment and scoped implementation. |
| U09 | Make Portfolio a concise triage page using existing projections | U01, U02, U04, U07, U08 | Pending current-source assessment and scoped implementation. |
| U10 | Reduce task-card clutter without hiding actionable facts | Independent | Pending current-source assessment and scoped implementation. |
| U11 | Guide template preparation through unresolved inputs first | Independent | Pending current-source assessment and scoped implementation. |
| U12 | Attach authorized resolution guidance to lifecycle blockers | Independent | Pending current-source assessment and scoped implementation. |
| U13 | Distinguish idempotency conflicts from stale revisions in command guidance | Independent | Implemented; focused regression and production build verified. Integrated browser/role acceptance remains pending. |
| U14 | Make an exact linked task usable without locating its queue page | U01 | Pending current-source assessment and scoped implementation. |
| V01 | Qualify the changed journeys and record activation boundaries | U01, U02, U03, U04, U05, U06, U07, U08, U09, U10, U11, U12, U13, U14 | Pending integrated final-source CI, built-in browser journeys, responsive/keyboard checks and qualified activation evidence. |

## Implemented behavior

Package selection preview reports selected packages and directs users to current workspace actions. Partner guidance names the currently assigned Engagement Partner. Selection is not an eligibility decision; no decision is recorded by preview. Existing fresh-queue selection and session invalidation remain.

Command recovery distinguishes request-identity conflicts from revision/generation changes. Unknown conflict codes receive neutral guidance. Existing validation messages, unknown-outcome handling, session fences and single-send behavior remain. No new key, automatic retry or professional authority was introduced.

## Remaining acceptance

Next inspect bounded package-review discovery, then navigation/context and dependent My Work packets. Preserve authorization before paging, unavailable versus zero, command freshness, exact task-link identity and existing financial boundaries. Each packet retains its own acceptance criteria from the attachments. D00 blocks only true cross-firm switching. No tenant feature switch is authorized by this register. V01 and professional/external release gates remain open.
