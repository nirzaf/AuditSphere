# Client progress tracker

**Status:** CURRENT

**Purpose:** How AuditSphere measures and shows engagement progress at the top of the Engagement and Client workspaces and in the portfolio summary. Progress is a read-only projection of persisted records. It never changes task, lifecycle, review or release state, and it is not a second workflow state machine.

Progress detail and paged summaries read their task, evidence, hold, release and scope data within a repeatable-read snapshot. Authorization is checked again after the snapshot closes; a pre-existing caller transaction is refused rather than nested. This is a coherent read point, not a claim that source records cannot change immediately afterward.

## What is tracked

Progress is calculated per engagement. An engagement is one client, service route and reporting period, so the same client's annual audit, quarterly review or bookkeeping service each have their own figure. Nothing is averaged across engagements; the client and portfolio views list each engagement's own figure.

## Step classification

Each engagement task comes from its pinned workflow template step, or from a manual task. Templates are versioned and independently reviewed through the existing template workflow; adding or changing a condition creates a new revision with a new digest.

Ordinary Time tasks with the exact firm, client and engagement are included even when they have no workflow binding. They count as mandatory commitments, retain their own status, assignee, due date and budget phase, and are never copied into invented bindings. Client-only tasks and another engagement's tasks do not enter that engagement's figure. Workflow-bound tasks appear once; unbound tasks follow them in stable creation order. An outstanding unbound task links to the existing time workspace with its exact task ID.

| Template step | Classification | Counts toward percentage |
| --- | --- | --- |
| Required, no applicability condition | Mandatory | Always |
| Required, with an "Applies when" condition (at most 500 characters) | Conditional | Until a current independent task decision and any required professional decisions permit exclusion |
| Not required | Optional | Never; shown separately as "x of y" |
| Manual task with no template step | Mandatory | Always |

An operational cancellation, including a reason beginning `Not applicable:`, remains in immutable task activity but is not an independently reviewed applicability decision. Without a current independent task decision, a cancelled conditional step remains blocked and in the required denominator. No historical cancellation is migrated into an approval. A cancelled mandatory step also stays blocked. Conditions are only allowed on required steps.

`PracticeTimeService.WorkflowApplicability.cs` retains a conditional task proposal and an independent Manager, Partner or Administrator disposition under the existing scoped workflow-management and session rules. The proposal pins the published template, task revision, service/profile/period, generation and exact independently accepted PBC file receipt. It does not interpret the condition. Approval rechecks that basis under the existing safety locks; return and revocation can withdraw stale evidence under current authority. A newer unreviewed proposal, returned or revoked approval, changed basis or task hold restores the required denominator. The operational task status and strict task-completed dependencies remain unchanged.

For a conditional task with required `ReviewedProcedure` evidence, each exact linked procedure also needs a current independent `NotApplicableApproved` decision. Pending, returned, unrelated or stale procedure decisions remain distinct blockers and cannot substitute for the missing task decision. Neither task cancellation nor progress makes a professional decision or satisfies dependent prerequisites.

## Calculation

`Domain/Practice/EngagementProgress.cs` (`EngagementProgressCalculator`) is a pure calculator over facts read by `Application/Practice/EngagementProgressQuery.cs`:

- **Completed:** a completed task whose evidence prerequisites and directly linked PBC request are still current. A completed task with stale, missing or no-longer-accepted evidence is blocked, even if its operational status remains completed. Currentness propagates through required dependencies, so a completed successor is blocked when any prerequisite in its dependency chain is no longer current. Persisted completion history remains unchanged.
- **Blocked:** an open task waiting on prerequisite tasks; a cancelled required task without a current explicit non-applicability decision; a completed task with stale evidence.
- **Pending:** every other open or in-progress task, annotated with rework, waiting-for-client or pending-evidence conditions.
- **Percent complete:** completed ÷ applicable required steps, rounded down. It is empty when the plan has no applicable required step, and capped at 99% while any gate is open.

Gates come from existing sources and explicit unresolved policy boundaries. An open hold remains a gate even when the derived lifecycle omits it; the tracker does not disclose restricted hold reasons. On the financial statement audit route they include the derived eleven-stage lifecycle's current blockers, plus an open gate until the engagement reaches stage 9, Deliverable Release. If the lifecycle cannot be read, the gate is treated as open. Other routes remain gated while their service-specific completion profile is unapproved (decision D03), even if an issued release exists; missing releases and open holds are separate gates. A complete task plan therefore never reads as 100% while a gate is open, and 100% is never a release approval.

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

The conditional work-queue action opens `workflow-task-applicability.ts`. It reuses the scoped PBC source picker, shows exact retained/current basis text and requires a separate review with fresh assent. An unconfirmed command keeps its actor/firm/task recovery key and blocks another submission; recovery only reads that owner's retained receipt. Receipts and immutable paged history establish what was recorded, never current applicability. New applicability HTTP revisions and versions are canonical positive Int64 strings; opaque basis JSON is displayed without numeric parsing or reserialization.

## Endpoints

- `GET /api/ui/engagements/{id}/progress`
- `GET /api/ui/clients/{id}/progress?page=` (optional zero-based page; omitted means the first page)
- `GET /api/ui/portfolio/progress?page=`
- `GET /api/ui/engagements/{engagementId}/workflow/tasks/{taskId}/applicability` (optional exact source request or historical proposal)
- `GET /api/ui/engagements/{engagementId}/workflow/tasks/{taskId}/applicability/history` (bounded complete keyset cursor)
- `GET /api/ui/engagements/{engagementId}/workflow/tasks/{taskId}/applicability/receipts/{key}` (current actor's request recovery)
- `POST /api/ui/engagements/{engagementId}/workflow/tasks/{taskId}/applicability/proposals`
- `POST /api/ui/engagements/{engagementId}/workflow/tasks/{taskId}/applicability/decisions`

These task routes retain the existing authenticated, feature-filtered boundary and CSRF-protected command transport. `WorkflowTaskApplicabilityQuery` reads independent snapshots, rechecks authority before disclosure and batches source facts with engagement/client keys. PostgreSQL retains proposal/decision lineage, independent actors and append-only evidence; no task or professional approval is backfilled. Release and professional acceptance remain separate from implementation and focused verification.
