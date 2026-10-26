# Client progress tracker

**Status:** CURRENT

**Purpose:** How AuditSphere measures and shows engagement progress at the top of the Engagement and Client workspaces and in the portfolio summary. Progress is a read-only projection of persisted records. It never changes task, lifecycle, review or release state, and it is not a second workflow state machine.

## What is tracked

Progress is calculated per engagement. An engagement is one client, service route and reporting period, so the same client's annual audit, quarterly review or bookkeeping service each have their own figure. Nothing is averaged across engagements; the client and portfolio views list each engagement's own figure.

## Step classification

Each engagement task comes from its pinned workflow template step, or from a manual task. Templates are versioned and independently reviewed through the existing template workflow; adding or changing a condition creates a new revision with a new digest.

Ordinary Time tasks with the exact firm, client and engagement are included even when they have no workflow binding. They count as mandatory commitments, retain their own status, assignee, due date and budget phase, and are never copied into invented bindings. Client-only tasks and another engagement's tasks do not enter that engagement's figure. Workflow-bound tasks appear once; unbound tasks follow them in stable creation order. An outstanding unbound task links to the existing time workspace with its exact task ID.

| Template step | Classification | Counts toward percentage |
| --- | --- | --- |
| Required, no applicability condition | Mandatory | Always |
| Required, with an "Applies when" condition (at most 500 characters) | Conditional | Unless recorded as not applicable |
| Not required | Optional | Never; shown separately as "x of y" |
| Manual task with no template step | Mandatory | Always |

A conditional step is recorded as not applicable only through an authorized cancellation with an explicit `Not applicable: <reason>` decision in the current binding revision's immutable `TASK_CANCELLED` activity. A generic cancellation, blank decision or historical cancellation does not waive a required step. A cancelled mandatory step is never waived; it stays blocked until reopened and completed. Conditions are only allowed on required steps. Non-applicability does not itself approve professional work or satisfy dependent task prerequisites.

When a cancelled conditional task has required `ReviewedProcedure` evidence rules, every exact linked procedure must also have a current independent `NotApplicableApproved` decision from the existing audit applicability workflow. A decision on another procedure, a pending or returned decision, or approval made stale by changed inputs leaves the task blocked and in the denominator. An operational cancellation cannot supply those professional decisions. Unlinked operational conditional steps still use the explicit current cancellation decision.

## Calculation

`Domain/Practice/EngagementProgress.cs` (`EngagementProgressCalculator`) is a pure calculator over facts read by `Application/Practice/EngagementProgressQuery.cs`:

- **Completed:** a completed task whose evidence prerequisites and directly linked PBC request are still current. A completed task with stale, missing or no-longer-accepted evidence is blocked, even if its operational status remains completed. Currentness propagates through required dependencies, so a completed successor is blocked when any prerequisite in its dependency chain is no longer current. Persisted completion history remains unchanged.
- **Blocked:** an open task waiting on prerequisite tasks; a cancelled required task without a current explicit non-applicability decision; a completed task with stale evidence.
- **Pending:** every other open or in-progress task, annotated with rework, waiting-for-client or pending-evidence conditions.
- **Percent complete:** completed ÷ applicable required steps, rounded down. It is empty when the plan has no applicable required step, and capped at 99% while any gate is open.

Gates come from existing sources only. An open hold remains a gate even when the derived lifecycle omits it; the tracker does not disclose restricted hold reasons. On the financial statement audit route they include the derived eleven-stage lifecycle's current blockers, plus an open gate until the engagement reaches stage 9, Deliverable Release. If the lifecycle cannot be read, the gate is treated as open. On other routes they are open holds, plus an open gate until an issued release exists. Non-audit routes also carry a disclosure that their lifecycle gates are not yet specified (decision D03). A complete task plan therefore never reads as 100% while a professional gate is open, and 100% is never a release approval.

The view also gives the current phase, the responsible person or role, the earliest outstanding due date and the next action. The next action is the first pending step, or the first gate's existing deep link.

## Access and freshness

The query recalculates on every read, so there is no stored figure to go stale. Mounted visible trackers refresh every 30 seconds and on window focus return; hidden or destroyed widgets stop reads. A background refresh keeps expanded step details and focus while displaying its loading state. Cached figures clear on a changed record, actor, firm, session generation, denied access or unsupported response. Reads use the workflow-task evidence read roles and rechecked engagement authorization (`InternalOnly`). Client pages contain at most 50 engagements, newest period first; portfolio pages contain at most 10. Both apply the narrower progress-reading scope before counting or paging, so inaccessible engagements cannot inflate totals or displace permitted periods. A client read requires current client coverage or a permitted engagement; a cursor grants no authority. The client UI exposes previous/next navigation and moves keyboard focus to its persistent progress heading before loading another page. Before returning a progress summary, the query rechecks every returned engagement against the progress-reading roles; retaining a broader portfolio role cannot preserve revoked progress access. Each engagement is read in stable ordinal/binding order with at most 501 rows as an overflow witness, then limited to 500 projected steps. Only an oversized engagement receives the open read-bound gate; another engagement cannot consume its window or inherit that gate. Detail, client and portfolio views use the same per-engagement cap, so an oversized plan remains visibly incomplete everywhere. Within that plan bound, evidence is evaluated in pages of at most 100 bindings, retaining the existing per-task evidence-rule bound. Client and portfolio summaries use the same batching across engagements.

Bound and unbound task sources each use a bounded overflow witness. Their combined projection is still limited to 500 steps per engagement; adding ordinary tasks cannot silently exceed that cap or put an oversized plan's gate on its neighbour.

## Presentation

`src/AuditSphereOps.Ui/src/app/features/practice/engagement-progress.ts` holds the decoder and three components:

- `audit-engagement-progress` is the tracker panel near the top of the engagement workspace. It shows the percentage bar, counts, facts, open gates and a collapsible step table.
- `audit-client-progress` is the per-engagement summary on the client workspace.
- `audit-portfolio-progress` is the paged portfolio summary.

The decoder refuses figures that do not add up, a percentage above what the counts allow, 100% with an open gate, and a next-action link outside the application. The "Applies when" field is in the template editor (`workflow-templates.ts`). The plan preview (`engagement-task-plan.ts`) and the work queue (`work-queue.ts`) label each template task Required, Conditional or Optional and show its condition, so the person recording a step as not applicable sees why it may not apply.

## Endpoints

- `GET /api/ui/engagements/{id}/progress`
- `GET /api/ui/clients/{id}/progress?page=` (optional zero-based page; omitted means the first page)
- `GET /api/ui/portfolio/progress?page=`
