import { NgTemplateOutlet } from '@angular/common';
import { afterNextRender, Component, DestroyRef, effect, ElementRef, HostListener, inject, Injector, signal, untracked, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, NavigationEnd, ParamMap, Router, RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import type { CommandOutcome } from '../../core/api';
import { arr, bool, date, guid, instant, int, nullable, obj, oneOf, str, text } from '../../core/decode';
import { SHARED } from '../../core/ui';
import { SessionService } from '../../core/session';
import { WorkspaceAvailability } from '../../core/workspace-availability';
import { QUEUE_DISPLAYS, QueueDisplay, boardColumns, groupedTasks, queueCalendar } from './work-queue-views';
import { hasUnsupportedQueueContext, parseWorkQueueLocation, type QueueOwner, type QueueView, type WorkQueueLocation } from './work-queue-location';
import { WorkflowTaskDependenciesPanel } from './workflow-task-dependencies';
import { WorkflowTaskEvidencePanel } from './workflow-task-evidence';
import { WorkflowTaskApplicabilityPanel } from './workflow-task-applicability';
import { canReopenTask, decodeWorkflowTaskWorkspace, WorkflowTaskCancellationPanel } from './workflow-task-cancellation';
import type { WorkflowTaskRecoveryAction } from './workflow-task-cancellation';
import { WorkQueueActionRegistry, type WorkQueueDispatchTicket, type WorkQueuePendingAction, type WorkQueueRegistryOwner,
  type WorkQueueReconciliation } from './work-queue-action-registry';
import { WORK_QUEUE_RECEIPT_SCAN_MAX_CHARS, WORK_QUEUE_RECEIPT_SCAN_MAX_ENTRIES,
  WorkQueueReceiptDiscovery, type WorkQueueReceiptCandidate } from './work-queue-receipt-discovery';

const nonBlank = (max: number) => (value: unknown, path: string): string => {
  const decoded = str(max)(value, path);
  if (!decoded.trim()) throw new Error(`${path}: expected non-empty text`);
  return decoded;
};
function optionalNullable<T>(decoder: (value: unknown, path: string) => T) {
  return (value: unknown, path: string): T | null => value === undefined ? null : nullable(decoder)(value, path);
}
const queueTaskShape = obj({
  taskId: guid, clientId: guid, clientName: text, engagementId: guid, engagementServiceRoute: str(100),
  engagementPeriod: str(80), title: str(300), phase: str(40), ordinal: int, taskStatus: str(40),
  assigneeUserId: nullable(guid), assigneeDisplayName: nullable(str(200)), dueDate: nullable(date),
  mappingVersionId: nullable(guid), fsliCode: nullable(str(100)), unmetTaskDependencies: int, unmetEvidencePrerequisites: int,
  canComplete: bool, canChangeDueDate: bool, canAssign: bool, canCancel: nullable(bool), canReopen: nullable(bool), revision: int,
  sourceKey: str(500), templateRevisionId: nullable(guid), templateStepId: nullable(guid),
  requiredTask: nullable(bool), taskGroupName: optionalNullable(str(120)), applicabilityCondition: optionalNullable(str(500)),
  pbcRequestId: nullable(guid), pbcState: nullable(str(32)),
  reviewState: nullable(str(30)), reviewComment: nullable(str(20000)),
  onHold: optionalNullable(bool), holdReason: optionalNullable(str(2000)),
  canSuspend: optionalNullable(bool), canResume: optionalNullable(bool), manualRequiredTask: optionalNullable(bool),
});
/** Older API payloads omit server-projected recovery affordances; absence always fails closed. */
const queueTask = (value: unknown, path: string) => {
  const decoded = queueTaskShape(value, path);
  return { ...decoded, canCancel: decoded.canCancel ?? false, canReopen: decoded.canReopen ?? false,
    onHold: decoded.onHold ?? false, canSuspend: decoded.canSuspend ?? false, canResume: decoded.canResume ?? false };
};
type QueueTask = ReturnType<typeof queueTask>;
/** Readiness presentation from server-evaluated facts; never inferred from viewer action permissions. */
export function hasBlockingPrerequisites(task: Pick<QueueTask,
  'taskStatus' | 'unmetTaskDependencies' | 'unmetEvidencePrerequisites' | 'pbcRequestId' | 'pbcState'>): boolean {
  return ['OPEN', 'IN_PROGRESS'].includes(task.taskStatus) &&
    (task.unmetTaskDependencies > 0 || task.unmetEvidencePrerequisites > 0 ||
      task.pbcRequestId !== null && !['ACCEPTED', 'CLOSED'].includes(task.pbcState ?? ''));
}

/** Work overview cards in display order; each opens its server queue view through selectView. */
const summaryCards: { key: QueueView; label: string }[] = [
  { key: 'MY_TASKS', label: 'My tasks' },
  { key: 'DUE_TODAY', label: 'Due today' },
  { key: 'OVERDUE', label: 'Overdue' },
  { key: 'WAITING_FOR_CLIENT', label: 'Waiting for client' },
  { key: 'UNDER_REVIEW', label: 'Under review' },
  { key: 'CHANGES_REQUIRED', label: 'Rework required' },
  { key: 'UPCOMING', label: 'Next 7 days' },
];
/** Exact non-negative count kept as its JSON text; never converted to a JavaScript number. */
const exactCount = (value: unknown, path: string): string => {
  if (typeof value === 'string' && /^(0|[1-9][0-9]{0,18})$/.test(value)) return value;
  throw new Error(`${path}: expected exact count`);
};
const summaryShape = obj({ asOfDateUtc: date, counts: arr(obj({ view: str(30), count: exactCount }), 7) });
/** Complete authorized counts for every work overview view; duplicate, missing or unexpected views are refused. */
export const decodeQueueSummary = (value: unknown, path: string) => {
  const decoded = summaryShape(value, path);
  const counts = new Map<string, string>(decoded.counts.map(x => [x.view, x.count]));
  if (counts.size !== decoded.counts.length || counts.size !== summaryCards.length || summaryCards.some(x => !counts.has(x.key)))
    throw new Error(`${path || 'summary'}: expected one count for each work overview view`);
  return { asOfDateUtc: decoded.asOfDateUtc, cards: summaryCards.map(x => ({ ...x, count: counts.get(x.key)! })) };
};
const decodeQueue = obj({
  view: str(30), tasks: arr(queueTask, 100), hasMore: bool,
  nextCursor: nullable(obj({ dueDate: nullable(date), taskId: guid })),
  summary: nullable(decodeQueueSummary),
});
type ReminderTaskSelection = { engagementId: string; taskId: string };
const reminderSourceShape = obj({
  sourceKind: oneOf('TASK_DUE', 'PBC_DUE'), sourceId: guid, sourceRevision: int,
  dueRevision: int, sourceDueDate: date, sourceHref: str(300), recipientUserId: guid,
  isSourceCurrent: nullable(bool),
});
const reminderSource = (value: unknown, path: string) => {
  const decoded = reminderSourceShape(value, path);
  if (!value || typeof value !== 'object' || Array.isArray(value) ||
      !Object.prototype.hasOwnProperty.call(value, 'isSourceCurrent') ||
      (value as Record<string, unknown>)['isSourceCurrent'] === undefined)
    throw new Error(`${path}.isSourceCurrent: required nullable field`);
  return decoded;
};
const optionalBoolean = (value: unknown, path: string): boolean => value === undefined ? false : bool(value, path);
const reminderTaskRowShape = obj({
  id: guid, title: nonBlank(300), phase: nonBlank(40), ordinal: int, status: nonBlank(40),
  assigneeUserId: nullable(guid), assigneeDisplayName: nullable(str(200)), assignedRole: nullable(str(100)),
  dueDate: nullable(date), revision: int, reminderRuleId: nullable(guid),
  reminderRuleRevisionId: nullable(guid), reminderOperationId: nullable(guid),
  reminderSource: optionalNullable(reminderSource), canOpenReminderRule: optionalBoolean,
  onHold: optionalBoolean,
  canComplete: optionalBoolean,
  canChangeDueDate: optionalBoolean,
  canAssign: optionalBoolean,
  canCancel: optionalBoolean,
  canReopen: optionalBoolean,
  canSuspend: optionalBoolean,
  canResume: optionalBoolean,
  unmetTaskDependencies: (v: unknown, p: string) => v === undefined ? 0 : int(v, p),
  unmetEvidencePrerequisites: (v: unknown, p: string) => v === undefined ? 0 : int(v, p),
});
const reminderTaskRow = (value: unknown, path: string) => reminderTaskRowShape(value, path);
const reminderTaskWorkspaceShape = obj({ engagementId: guid, tasks: arr(reminderTaskRow, 500), truncated: bool });
type ReminderTaskRow = ReturnType<typeof reminderTaskRow>;
type SelectedReminderTask = ReminderTaskRow & { engagementId: string };
type ReminderSource = NonNullable<ReminderTaskRow['reminderSource']>;
function reminderSourceHrefAllowed(engagementId: string, source: ReminderSource): boolean {
  const engagement = engagementId.toLowerCase();
  const sourceId = source.sourceId.toLowerCase();
  return source.sourceKind === 'TASK_DUE'
    ? source.sourceHref === `/app/work?engagementId=${engagement}&taskId=${sourceId}`
    : source.sourceHref === `/app/engagements/${engagement}/pbc`;
}
function decodeReminderTaskWorkspace(value: unknown, path: string, selection: ReminderTaskSelection | null): SelectedReminderTask {
  const workspace = reminderTaskWorkspaceShape(value, path);
  const task = workspace.tasks[0];
  if (!selection || workspace.engagementId.toLowerCase() !== selection.engagementId || workspace.truncated ||
      workspace.tasks.length !== 1 || task.id.toLowerCase() !== selection.taskId)
    throw new Error(`${path || 'task'}: selected task does not match the current reminder link`);
  if (task.reminderSource && !reminderSourceHrefAllowed(selection.engagementId, task.reminderSource))
    throw new Error(`${path || 'task'}: reminder source link does not match its authorized context`);
  return { ...task, engagementId: workspace.engagementId.toLowerCase() };
}
const decodeAssigneeOptions = arr(obj({ userId: guid, displayName: nonBlank(200) }), 200);
type AssignmentIntent = { task: QueueTask; owner: { userId: string; displayName: string }; action: 'assign' | 'reassign'; scope: string };
type AssignmentObservation = ReturnType<typeof decodeWorkflowTaskWorkspace>['tasks'][number];
type VerifiedReceipt = { candidate: WorkQueueReceiptCandidate; owner: string; revision: number };
const reviewItem = obj({ resultId: guid, procedureId: guid, engagementId: guid, clientName: text,
  serviceRoute: str(100), engagementPeriod: str(80), sourceProcedureId: str(100), procedureTitle: text,
  revision: int, preparedByDisplayName: str(200), submittedAt: instant });
const decodeReviewInbox = obj({ reviews: arr(reviewItem, 100), hasMore: bool,
  nextCursor: nullable(obj({ submittedAt: instant, resultId: guid })) });
const activityEventShape = obj({
  id: guid, kind: oneOf('TASK_CREATED', 'TASK_ASSIGNED', 'TASK_REASSIGNED', 'TASK_COMPLETED', 'TASK_CANCELLED', 'TASK_REOPENED', 'TASK_SUSPENDED', 'TASK_RESUMED', 'TASK_DUE_DATE_CHANGED', 'TASK_EVIDENCE_RULE_CHANGED', 'TASK_DEPENDENCIES_CHANGED'),
  sourceLabel: oneOf('Operational Task'), actorDisplayName: nonBlank(200),
  assigneeDisplayName: nullable(str(200)), taskRevision: int, reason: nullable(str(2000)),
  previousDueDate: nullable(date), newDueDate: nullable(date), occurredAt: instant,
});
const activityEvent = (value: unknown, path: string) => {
  const event = activityEventShape(value, path);
  if (event.kind === 'TASK_CANCELLED' && !event.reason?.trim())
    throw new Error(`${path}.reason: expected non-empty cancellation reason`);
  if (event.kind === 'TASK_REOPENED' && !event.reason?.trim())
    throw new Error(`${path}.reason: expected non-empty reopening reason`);
  if ((event.kind === 'TASK_SUSPENDED' || event.kind === 'TASK_RESUMED') && !event.reason?.trim())
    throw new Error(`${path}.reason: expected non-empty hold reason`);
  return event;
};
export const decodeActivity = obj({
  engagementId: guid, taskId: guid, from: date, to: date, events: arr(activityEvent, 101),
  hasMore: bool, nextCursor: nullable(obj({ occurredAt: instant, activityId: guid })),
});

const queueViews: { key: QueueView; label: string }[] = [
  { key: 'MY_TASKS', label: 'My tasks' },
  { key: 'TEAM', label: 'Team' },
  { key: 'DUE_TODAY', label: 'Due today' },
  { key: 'UPCOMING', label: 'Next 7 days' },
  { key: 'WAITING_FOR_CLIENT', label: 'Waiting for client' },
  { key: 'UNDER_REVIEW', label: 'Under review' },
  { key: 'CHANGES_REQUIRED', label: 'Changes required' },
  { key: 'OVERDUE', label: 'Overdue' },
  { key: 'ON_HOLD', label: 'On hold' },
  { key: 'COMPLETED', label: 'Completed' },
  { key: 'CANCELLED', label: 'Cancelled' },
];

@Component({
  selector: 'audit-work-queue',
  imports: [MatButtonModule, RouterLink, NgTemplateOutlet, WorkflowTaskEvidencePanel, WorkflowTaskApplicabilityPanel, WorkflowTaskDependenciesPanel, WorkflowTaskCancellationPanel, ...SHARED],
  template: `
    <audit-page-header title="My work" eyebrow="Engagement tasks"
      description="Operational tasks across your current client and engagement grants. Task completion never replaces audit review or engagement approval." />
    <p id="work-table-scroll-instructions" class="sr-only">When a table region is focused, use the left and right arrow keys to scroll horizontally. Home and End move to the start and end.</p>
    @if (availability.state() === 'loading') { <p role="status">Checking available work.</p> }
    @if (availability.state() === 'unavailable') { <p role="alert">Work availability is temporarily unavailable. <button matButton (click)="availability.refresh()">Retry</button></p> }
    @if (availability.current(); as access) {
      @if (access.tasks === 'not_enabled' && access.reviews === 'not_enabled') { <p role="status">Workflow work is not enabled on this server.</p> }
    }
    @if (availability.current()?.destinations?.includes('reviews')) {
      <section aria-labelledby="package-review-handoff-heading">
        <h2 id="package-review-handoff-heading">Financial package reviews</h2>
        <p>Package review is separate from operational tasks and procedure submissions. Open the authorized queue to check current stages and older packages; no package total is loaded here.</p>
        <a matButton routerLink="/app/accounting/reviews">Open financial package review queue</a>
        <p>Each package rechecks your current review eligibility. Opening the queue records no approval.</p>
      </section>
    }
    @if (taskRefused()) { <p role="alert">The task queue is unavailable in your current scope. <button matButton (click)="retrySection('tasks')">Retry task queue</button></p> }
    @if (reviewRefused()) { <p role="alert">The review inbox is unavailable in your current scope. <button matButton (click)="retrySection('reviews')">Retry review inbox</button></p> }
    @if (priorSessionFenceAvailable()) {
      <p>Recovery storage from an earlier session needs a local check.
        <button matButton (click)="releasePriorSessionFence()">Check earlier session storage</button></p>
    }
    @if (priorFenceMessage()) { <p role="status">{{ priorFenceMessage() }}</p> }
    @if (taskAccessCurrent()) {
    @if (availability.current()?.templates === 'available') { <p><a matButton routerLink="/app/practice/workflow-templates">Manage workflow templates</a></p> }
    }
    @if (taskAccessCurrent() && queueLocationValid()) {
    <section class="work-summary" aria-labelledby="work-summary-heading">
      <h2 id="work-summary-heading">Work overview</h2>
      <div role="status">@if (queue.loading()) { <p>Loading work overview.</p> }</div>
      @if (queue.data(); as result) {
        @if (result.summary; as summary) {
          <p>As of {{ summary.asOfDateUtc }} (UTC)</p>
          <p class="queue-note">Counts cover each default authorized view and may overlap. Assignment filters affect the list below.</p>
          <div class="summary-cards">
            @for (card of summary.cards; track card.key) {
              <button type="button" class="summary-card" [attr.aria-pressed]="selectedView() === card.key" (click)="selectView(card.key)">
                <span class="summary-label">{{ card.label }}</span>
                <span class="summary-count">{{ groupCount(card.count) }}</span>
              </button>
            }
          </div>
        } @else {
          <p class="queue-note">Summary unavailable — refresh after the dashboard API is updated.</p>
        }
      }
    </section>
    }
    @if (reviewAccessCurrent()) {
    <section aria-labelledby="review-inbox-heading">
      <h2 id="review-inbox-heading">Submitted work awaiting review</h2>
      <p>These submissions are not represented in Under review. Earlier operational tasks may still exist. Opening fieldwork rechecks review eligibility and current evidence.</p>
      <audit-state [loading]="reviews.loading()" [error]="reviews.error()" label="review inbox" />
      @if (reviews.data(); as inbox) {
        <p role="status">{{ inbox.reviews.length }} current submission(s) awaiting review outside the task queue.</p>
        <div class="table-scroll" role="region" aria-label="Submitted procedure reviews" aria-describedby="work-table-scroll-instructions" tabindex="0" (keydown)="scrollWorkTable($event)"><table>
          <caption>Current procedure submissions awaiting independent review</caption>
          <thead><tr><th scope="col">Submitted</th><th scope="col">Client / engagement</th><th scope="col">Procedure</th><th scope="col">Prepared by</th><th scope="col"><span class="sr-only">Action</span></th></tr></thead>
          <tbody>
            @for (item of inbox.reviews; track item.resultId) {
              <tr>
                <td><time [attr.datetime]="item.submittedAt">{{ item.submittedAt }}</time><small>Revision {{ item.revision }}</small></td>
                <td><a [routerLink]="['/app/engagements', item.engagementId, 'audit-fieldwork']">{{ item.clientName }}</a><small>{{ item.serviceRoute }} · {{ item.engagementPeriod }}</small></td>
                <td>{{ item.procedureTitle }}<small>{{ item.sourceProcedureId }}</small></td>
                <td>{{ item.preparedByDisplayName }}</td>
                <td><a matButton [routerLink]="['/app/engagements', item.engagementId, 'audit-fieldwork']">Open fieldwork</a></td>
              </tr>
            } @empty {
              <tr><td colspan="5">No additional current submissions await review in your authorized scope.</td></tr>
            }
          </tbody>
        </table></div>
        @if (inbox.hasMore && inbox.nextCursor) {
          <button matButton (click)="nextReviewPage(inbox.nextCursor)">Next reviews</button>
        }
      }
    </section>
    }
    @if (taskAccessCurrent()) {
    @if (queueLocationError()) {
      <p role="alert">{{ queueLocationError() }} <button matButton (click)="resetQueueLocation()">Reset queue filters</button></p>
    }
    <audit-state [loading]="queue.loading()" [error]="queue.error()" label="work queue" />
    <section aria-label="Saved task receipts">
      <button matButton (click)="findSavedReceipts()" [disabled]="receiptChecking()">Find saved task receipts</button>
      @if (receiptChecking()) { <p role="status">Checking saved receipts against current task access.</p> }
      @if (receiptMessage()) { <p role="status">{{ receiptMessage() }}</p> }
      @for (entry of visibleSavedReceipts(); track entry.candidate.storageKey) {
        <p>Unconfirmed {{ entry.candidate.kind }} receipt for task {{ entry.candidate.taskId }}.
          <button matButton (click)="openSavedReceipt(entry)" [disabled]="receiptChecking()">Open saved receipt</button></p>
      }
      @if (hasMoreSavedReceipts()) {
        <button matButton (click)="checkMoreSavedReceipts()" [disabled]="receiptChecking()">Check more saved receipts</button>
      }
    </section>
    @if (reminderTaskLinkError()) { <p role="alert">{{ reminderTaskLinkError() }}</p> }
    @if (reminderTaskSelection(); as selection) {
      <section class="activity-panel" aria-labelledby="reminder-task-heading">
        <h2 id="reminder-task-heading" tabindex="-1">Task selected from deadline reminder</h2>
        @if (reminderTask.loading()) { <p role="status">Loading the exact selected task.</p> }
        @if (reminderTask.error()) { <p role="alert">{{ reminderTask.error() }}</p> }
        @if (currentReminderTask(); as selected) {
          <p role="status">{{ selected.title }} loaded from the reminder link; status {{ selected.status }}, revision {{ selected.revision }}.</p>
          <dl class="facts">
            <dt>Task</dt><dd>{{ selected.title }} · {{ selected.id }}</dd>
            <dt>Engagement</dt><dd>{{ selection.engagementId }}</dd>
            <dt>Phase</dt><dd>{{ selected.phase }} · #{{ selected.ordinal }}</dd>
            <dt>Due date</dt><dd>{{ selected.dueDate ?? 'No due date' }}</dd>
            <dt>Owner</dt><dd>{{ selected.assigneeDisplayName ?? selected.assigneeUserId ?? 'Unassigned' }}</dd>
            <dt>Status</dt><dd><audit-status [value]="selected.status" /> · revision {{ selected.revision }}</dd>
          </dl>
          @if (selected.reminderSource || selected.reminderRuleRevisionId || selected.reminderOperationId) {
            <section class="activity-panel" aria-labelledby="reminder-origin-heading">
              <h3 id="reminder-origin-heading">Reminder origin</h3>
              <dl class="facts">
                @if (selected.reminderSource; as source) {
                  <dt>Source</dt><dd>{{ source.sourceKind === 'TASK_DUE' ? 'Workflow task due date' : 'PBC response due date' }} · {{ source.sourceId }}</dd>
                  <dt>Source revision</dt><dd>{{ source.sourceRevision }}</dd>
                  <dt>Due revision</dt><dd>{{ source.dueRevision }}</dd>
                  <dt>Source due date</dt><dd>{{ source.sourceDueDate }}</dd>
                  <dt>Recipient</dt><dd>{{ source.recipientUserId }}</dd>
                  <dt>Current source</dt><dd>{{ source.isSourceCurrent === null ? 'Current source status unavailable' : source.isSourceCurrent ? 'Source is current' : 'Source has changed' }}</dd>
                }
                @if (selected.reminderRuleRevisionId) {
                  <dt>Rule revision</dt><dd>{{ selected.reminderRuleRevisionId }}</dd>
                }
                @if (selected.reminderOperationId) {
                  <dt>Reminder operation</dt><dd>{{ selected.reminderOperationId }}</dd>
                }
              </dl>
              @if (selected.reminderSource; as source) {
                @if (reminderSourceTarget(source); as sourceTarget) {
                  <a matButton [routerLink]="sourceTarget.commands" [queryParams]="sourceTarget.queryParams">{{ sourceTarget.label }}</a>
                }
              }
              @if (selected.canOpenReminderRule && selected.reminderRuleId && selected.reminderRuleRevisionId) {
                <a matButton [routerLink]="['/app/practice/reminder-rules']"
                  [queryParams]="{ ruleId: selected.reminderRuleId, revisionId: selected.reminderRuleRevisionId }">
                  Open exact reminder rule revision
                </a>
              }
            </section>
          }
          <div role="group" aria-label="Selected task workspaces">
            <button matButton (click)="openLinkedTaskPanel('activity')">{{ activityTask()?.taskId.toLowerCase() === selected.id.toLowerCase() ? 'Hide activity' : 'View activity' }}</button>
            <button matButton (click)="openLinkedTaskPanel('evidence')">Review prerequisites</button>
            <button matButton (click)="openLinkedTaskPanel('ordering')">Review task ordering</button>
          </div>
          <p>Each workspace checks its own current permissions. Opening it does not complete or approve the task.</p>
          @if (reminderQueueTask(); as task) {
            @if (task.canChangeDueDate || task.canComplete || task.canCancel || reopenable(task) || task.canSuspend || task.canResume || task.canAssign) {
              <div role="group" aria-label="Actions authorized by the current work queue">
                @if (task.canChangeDueDate) {
                  <label class="sr-only" [attr.for]="'reminder-due-date-' + task.taskId">Due date for {{ task.title }}</label>
                  <input [id]="'reminder-due-date-' + task.taskId" type="date" [value]="task.dueDate ?? ''" #reminderDueDate />
                  <button matButton [disabled]="taskMutationBlocked(task.taskId)" (click)="changeDueDate(task.engagementId, task.taskId, task.revision, reminderDueDate.value)">Save date</button>
                }
                @if (task.canComplete) {
                  <button matButton="filled" (click)="complete(task.engagementId, task.taskId, task.revision)" [disabled]="taskMutationBlocked(task.taskId) || blockedPrerequisites(task) || task.onHold">Complete task</button>
                }
                @if (task.canCancel) {
                  <button matButton [attr.data-cancel-engagement]="task.engagementId" [attr.data-cancel-task]="task.taskId"
                    (click)="openCancellation(task, $event)">Cancel task</button>
                }
                @if (reopenable(task)) {
                  <button matButton [attr.data-reopen-engagement]="task.engagementId" [attr.data-reopen-task]="task.taskId"
                    (click)="openReopening(task, $event)">Reopen task</button>
                }
                @if (task.canSuspend || task.canResume) {
                  <button matButton [attr.data-hold-engagement]="task.engagementId" [attr.data-hold-task]="task.taskId"
                    (click)="openHold(task, $event)">{{ task.canResume ? 'Resume task' : 'Put on hold' }}</button>
                }
                @if (task.canAssign) { <button matButton (click)="openAssignment(task)">{{ task.assigneeUserId ? 'Reassign task' : 'Assign task' }}</button> }
              </div>
            } @else {
              <p role="status">The selected task is read-only; the current queue has no authorized action for it.</p>
            }
          } @else {
            <p role="status">The selected task is read-only for queue actions because no currently loaded queue row matches its current status and revision. Its activity and prerequisite workspaces can still be checked.</p>
          }
        }
      </section>
    }
    @if (queueLocationValid() && selectedView() !== 'MY_TASKS') {
      <label>Task assignment
        <select [value]="ownerFilter()" (change)="selectOwnerFilter($any($event.target).value)">
          <option value="ALL">All authorized assignments</option>
          <option value="ME">Assigned to me</option>
          <option value="UNASSIGNED">Unassigned</option>
        </select>
      </label>
    }
    @if (queueLocationValid()) {
    <nav class="queue-views" aria-label="Work queue views">
      @for (item of views; track item.key) {
        <button matButton [attr.aria-pressed]="selectedView() === item.key" (click)="selectView(item.key)">{{ item.label }}</button>
      }
    </nav>
    <nav class="queue-views" aria-label="Work queue display">
      @for (item of displays; track item.key) {
        <button matButton [attr.aria-pressed]="display() === item.key" (click)="selectDisplay(item.key)">{{ item.label }}</button>
      }
    </nav>
    }
    <ng-template #card let-task>
      <article class="task-card">
        <strong>{{ task.title }}</strong>
        <details class="task-source-details"><summary>Task source details</summary><small>Source: {{ task.sourceKey || 'Not recorded' }}@if (task.templateRevisionId) { · template revision {{ task.templateRevisionId }} }@if (task.templateStepId) { · step {{ task.templateStepId }} }</small>@if (task.mappingVersionId) { <small>mapping {{ task.mappingVersionId }}</small> }</details>
        @if (task.requiredTask !== null) { <small>Template task designation: {{ !task.requiredTask ? 'Optional' : task.applicabilityCondition ? 'Conditional' : 'Required' }}</small> }@if (task.requiredTask === null && task.manualRequiredTask !== null) { <small>Task designation: {{ task.manualRequiredTask ? 'Required' : 'Optional' }}</small> }@if (task.applicabilityCondition) { <small>Applies when: {{ task.applicabilityCondition }}</small> }
        @if (task.taskGroupName) { <small>Task group: {{ task.taskGroupName }}</small> }
        <small><a [routerLink]="['/app/engagements', task.engagementId]">{{ task.clientName }}</a> · {{ task.engagementServiceRoute }} · {{ task.engagementPeriod }}</small>
        <small>{{ task.phase.replaceAll('_', ' ') }} · #{{ task.ordinal }} · {{ task.dueDate ? 'due ' + task.dueDate : 'no due date' }} · {{ task.assigneeDisplayName ?? 'Unassigned' }}@if (task.fsliCode) { · FSLI {{ task.fsliCode }} }</small>
        @if (task.pbcRequestId) { <small><a [routerLink]="['/app/engagements', task.engagementId, 'pbc']">PBC request · {{ task.pbcState }}</a></small> }
        @if (task.taskStatus === 'OPEN' || task.taskStatus === 'IN_PROGRESS') { <small><a routerLink="/app/practice/time" [queryParams]="{ taskId: task.taskId }">Record time for this task</a></small> }
        @if (task.reviewState) {
          <small>{{ task.reviewState === 'UNDER_REVIEW' ? 'resubmitted work awaits independent review' : 'current audit result needs resubmission' }}</small>
          @if (task.reviewComment) { <small>Reviewer: {{ task.reviewComment }}</small> }
          @if (task.reviewState === 'CHANGES_REQUIRED' || task.reviewState === 'UNDER_REVIEW') {
            <small><a [routerLink]="['/app/engagements', task.engagementId, 'audit-fieldwork']">Open fieldwork</a></small>
          }
        }
        <small><audit-status [value]="task.taskStatus" />@if (task.onHold) { · <audit-status value="ON_HOLD" /> }@if (task.reviewState) { · <audit-status [value]="task.reviewState" /> }@if (task.pbcState) { · PBC {{ task.pbcState }} }</small>
        @if (task.onHold && task.holdReason) { <small>Hold reason: {{ task.holdReason }}</small> }
        @if (blockedPrerequisites(task)) { <small class="prerequisite-status">Prerequisites: <audit-status value="BLOCKED" /></small> }
        @if (task.unmetTaskDependencies) { <small>{{ task.unmetTaskDependencies }} task prerequisite(s) incomplete</small> }
        @if (task.unmetEvidencePrerequisites) { <small>{{ task.unmetEvidencePrerequisites }} evidence prerequisite(s) pending</small> }
        <button matButton (click)="selectEvidence(task)">Review prerequisites</button>
        @if (task.canComplete) {
          <button matButton="filled" (click)="complete(task.engagementId, task.taskId, task.revision)" [disabled]="taskMutationBlocked(task.taskId) || blockedPrerequisites(task) || task.onHold">Complete task</button>
        }
        <details class="task-more-actions"><summary>More task actions</summary>
        <button matButton (click)="showActivity(task.engagementId, task.taskId, task.title)">{{ activityTask()?.taskId === task.taskId ? 'Hide activity' : 'View activity' }}</button>

        @if (task.applicabilityCondition) { <button matButton (click)="selectApplicability(task)">Review conditional applicability</button> }
        <button matButton (click)="selectOrdering(task)">Review task ordering</button>

        @if (task.canCancel) {
          <button matButton [attr.data-cancel-engagement]="task.engagementId" [attr.data-cancel-task]="task.taskId"
            (click)="openCancellation(task, $event)">Cancel task</button>
        }
        @if (reopenable(task)) {
          <button matButton [attr.data-reopen-engagement]="task.engagementId" [attr.data-reopen-task]="task.taskId"
            (click)="openReopening(task, $event)">Reopen task</button>
        }
        @if (task.canSuspend || task.canResume) {
          <button matButton [attr.data-hold-engagement]="task.engagementId" [attr.data-hold-task]="task.taskId"
            (click)="openHold(task, $event)">{{ task.canResume ? 'Resume task' : 'Put on hold' }}</button>
        }
        @if (task.canAssign) { <button matButton (click)="openAssignment(task)"> {{ task.assigneeUserId ? 'Reassign task' : 'Assign task' }} </button> }
        </details>
      </article>
    </ng-template>
    @if (queueLocationValid() ? queue.data() : null; as result) {
      <p role="status">{{ labelFor(result.view) }}: {{ result.tasks.length }} {{ result.view === 'MY_TASKS' ? 'assigned to you' : 'in this authorized view' }}. Client and engagement rows are limited to your current grants.</p>
      @if (display() === 'BOARD') {
        <p class="queue-note">The board groups this page of tasks by operational status. A task changes column only through its own action; cards cannot be dragged.</p>
        <div class="board" role="group" [attr.aria-label]="labelFor(result.view) + ' board'">
          @for (column of board(result.tasks); track column.key) {
            <section class="board-column" [attr.aria-label]="column.label">
              <h2>{{ column.label }} <small>({{ column.tasks.length }})</small></h2>
              @for (task of column.tasks; track task.taskId) {
                <ng-container *ngTemplateOutlet="card; context: { $implicit: task }" />
              } @empty {
                <p class="queue-note">No tasks.</p>
              }
            </section>
          }
        </div>
      }
      @if (display() === 'CALENDAR') {
        @if (calendar(result.tasks); as cal) {
          <p class="queue-note">The calendar places this page of tasks on their due dates. Weeks run Sunday to Saturday; Friday and Saturday are the weekend.</p>
          @for (week of cal.weeks; track week.weekStart) {
            <section class="calendar-week" [attr.aria-label]="'Week of ' + week.weekStart">
              <h2>Week of {{ week.weekStart }}</h2>
              <div class="calendar-days">
                @for (day of week.days; track day.date) {
                  <div class="calendar-day" [class.weekend]="!day.workingDay">
                    <h3>{{ day.weekday }} <small>{{ day.date }}</small></h3>
                    @for (task of day.tasks; track task.taskId) {
                      <ng-container *ngTemplateOutlet="card; context: { $implicit: task }" />
                    }
                  </div>
                }
              </div>
            </section>
          } @empty {
            <p>No task on this page has a due date.</p>
          }
          @if (cal.undated.length) {
            <section class="calendar-week" aria-label="Tasks without a due date">
              <h2>No due date <small>({{ cal.undated.length }})</small></h2>
              <div class="calendar-days">
                @for (task of cal.undated; track task.taskId) {
                  <ng-container *ngTemplateOutlet="card; context: { $implicit: task }" />
                }
              </div>
            </section>
          }
        }
      }
      @if (display() === 'GROUPED') {
        <p class="queue-note">Grouped by engagement, workflow phase and template task group. Counts cover this page of tasks only; the engagement holds its complete task list.</p>
        @for (engagement of grouped(result.tasks); track engagement.engagementId) {
          <section class="queue-group" [attr.aria-label]="engagement.label">
            <h2><a [routerLink]="['/app/engagements', engagement.engagementId]">{{ engagement.label }}</a> <small>({{ engagement.count }} on this page)</small></h2>
            @for (phase of engagement.phases; track phase.phase) {
              <h3>{{ phase.label }} <small>({{ phase.count }})</small></h3>
              @for (group of phase.taskGroups; track group.name) {
                @if (group.name) { <h4>Task group: {{ group.name }}</h4> }
                @for (task of group.tasks; track task.taskId) {
                  <ng-container *ngTemplateOutlet="card; context: { $implicit: task }" />
                }
              }
            }
          </section>
        } @empty {
          <p>No tasks on this page.</p>
        }
      }
      @if (display() === 'LIST') {
      <div class="table-scroll" role="region" aria-label="Operational task list" aria-describedby="work-table-scroll-instructions" tabindex="0" (keydown)="scrollWorkTable($event)"><table class="work-task-table">
        <caption>{{ labelFor(result.view) }} — workflow tasks</caption>
        <thead><tr><th scope="col">Due</th><th scope="col">Client / engagement</th><th scope="col">Task</th><th scope="col">Owner</th><th scope="col">Operational status</th><th scope="col">Prerequisites</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
        <tbody>
          @for (task of result.tasks; track task.taskId) {
            <tr>
              <td>
                {{ task.dueDate ?? 'No due date' }}
                @if (task.canChangeDueDate) {
                  <div class="due-date-editor">
                    <label class="sr-only" [attr.for]="'due-date-' + task.taskId">Due date for {{ task.title }}</label>
                    <input #dueDateInput [id]="'due-date-' + task.taskId" type="date" [value]="task.dueDate ?? ''" />
                    <button matButton [disabled]="taskMutationBlocked(task.taskId)" (click)="changeDueDate(task.engagementId, task.taskId, task.revision, dueDateInput.value)">Save date</button>
                  </div>
                }
              </td>
              <td><a [routerLink]="['/app/engagements', task.engagementId]">{{ task.clientName }}</a><small>{{ task.engagementServiceRoute }} · {{ task.engagementPeriod }}</small></td>
              <td>{{ task.title }}<details class="task-source-details"><summary>Task source details</summary><small>Source: {{ task.sourceKey || 'Not recorded' }}@if (task.templateRevisionId) { · template revision {{ task.templateRevisionId }} }@if (task.templateStepId) { · step {{ task.templateStepId }} }</small>@if (task.mappingVersionId) { <small>mapping {{ task.mappingVersionId }}</small> }</details>@if (task.requiredTask !== null) { <small>Template task designation: {{ !task.requiredTask ? 'Optional' : task.applicabilityCondition ? 'Conditional' : 'Required' }}</small> }@if (task.requiredTask === null && task.manualRequiredTask !== null) { <small>Task designation: {{ task.manualRequiredTask ? 'Required' : 'Optional' }}</small> }@if (task.applicabilityCondition) { <small>Applies when: {{ task.applicabilityCondition }}</small> }@if (task.taskGroupName) { <small>Task group: {{ task.taskGroupName }}</small> }<small>{{ task.phase.replace('_', ' ') }} · #{{ task.ordinal }}@if (task.fsliCode) { · FSLI {{ task.fsliCode }} }</small>
                @if (task.pbcRequestId) { <small><a [routerLink]="['/app/engagements', task.engagementId, 'pbc']">PBC request · {{ task.pbcState }}</a></small> }
                @if (task.taskStatus === 'OPEN' || task.taskStatus === 'IN_PROGRESS') { <small><a routerLink="/app/practice/time" [queryParams]="{ taskId: task.taskId }">Record time for this task</a></small> }
                @if (task.reviewState) {
                  <small><audit-status [value]="task.reviewState" /> · {{ task.reviewState === 'UNDER_REVIEW' ? 'resubmitted work awaits independent review' : 'current audit result needs resubmission' }}</small>
                  @if (task.reviewComment) { <small>Reviewer: {{ task.reviewComment }}</small> }
                  @if (task.reviewState === 'CHANGES_REQUIRED' || task.reviewState === 'UNDER_REVIEW') {
                    <small><a [routerLink]="['/app/engagements', task.engagementId, 'audit-fieldwork']">Open fieldwork</a></small>
                  }
                }
              </td>
              <td>{{ task.assigneeDisplayName ?? 'Unassigned' }}</td>
              <td><audit-status [value]="task.taskStatus" />@if (task.onHold) { <audit-status value="ON_HOLD" /> }
                @if (task.onHold && task.holdReason) { <small>Hold reason: {{ task.holdReason }}</small> }
                @if (blockedPrerequisites(task)) { <small class="prerequisite-status">Prerequisites: <audit-status value="BLOCKED" /></small> }
              </td>
              <td>{{ task.unmetTaskDependencies ? task.unmetTaskDependencies + ' task prerequisite(s) incomplete' : 'No task prerequisites pending' }}
                @if (task.unmetEvidencePrerequisites) { <small>{{ task.unmetEvidencePrerequisites }} evidence prerequisite(s) pending</small> }
                <button matButton (click)="selectEvidence(task)">Review prerequisites</button>
              </td>
              <td>@if (task.canComplete) { <button matButton="filled" (click)="complete(task.engagementId, task.taskId, task.revision)" [disabled]="taskMutationBlocked(task.taskId) || blockedPrerequisites(task) || task.onHold">Complete task</button> }<details class="task-more-actions"><summary>More task actions</summary><button matButton (click)="showActivity(task.engagementId, task.taskId, task.title)">{{ activityTask()?.taskId === task.taskId ? 'Hide activity' : 'View activity' }}</button>
                @if (task.applicabilityCondition) { <button matButton (click)="selectApplicability(task)">Review conditional applicability</button> }
                <button matButton (click)="selectOrdering(task)">Review task ordering</button>
                @if (task.canCancel) { <button matButton [attr.data-cancel-engagement]="task.engagementId" [attr.data-cancel-task]="task.taskId"
                  (click)="openCancellation(task, $event)">Cancel task</button> }
                @if (reopenable(task)) { <button matButton
                  [attr.data-reopen-engagement]="task.engagementId" [attr.data-reopen-task]="task.taskId"
                  (click)="openReopening(task, $event)">Reopen task</button> }
                @if (task.canSuspend || task.canResume) { <button matButton
                  [attr.data-hold-engagement]="task.engagementId" [attr.data-hold-task]="task.taskId"
                  (click)="openHold(task, $event)">{{ task.canResume ? 'Resume task' : 'Put on hold' }}</button> }
                @if (task.canAssign) { <button matButton (click)="openAssignment(task)">{{ task.assigneeUserId ? 'Reassign task' : 'Assign task' }}</button> }</details></td>
            </tr>
          } @empty {
            <tr><td colspan="7">No tasks match this view in your current authorized scope.</td></tr>
          }
        </tbody>
      </table></div>
      }
      @if (assignmentTask(); as selected) {
        <section class="activity-panel" aria-labelledby="task-assignment-heading" [hidden]="!assignmentAccessCurrent()">
          <h2 id="task-assignment-heading">{{ selected.assigneeUserId ? 'Reassign workflow task' : 'Assign workflow task' }}</h2>
          @if (assignmentLoading()) { <p role="status">Loading eligible staff for this engagement.</p> }
          @if (assignmentError()) { <p role="alert">{{ assignmentError() }}</p> }
          <dl>
            <dt>Task</dt><dd>{{ selected.title }} ({{ selected.taskId }})</dd>
            <dt>Engagement</dt><dd>{{ selected.engagementPeriod }} · {{ selected.engagementId }}</dd>
            <dt>Current owner</dt><dd>{{ selected.assigneeUserId ? (selected.assigneeDisplayName ?? selected.assigneeUserId) : 'Unassigned' }}</dd>
          </dl>
          @if (assignmentUncertain()) {
            <p role="alert">The assignment outcome is unconfirmed. The original task, owner and revision are retained. Check current task and activity before another assignment.</p>
            <button matButton (click)="recoverAssignment()" [disabled]="assignmentBusy() || assignmentLoading()">Check current task and activity</button>
            @if (assignmentObservation(); as observed) {
              <p role="status">Observed owner: {{ observed.assigneeDisplayName ?? observed.assigneeUserId ?? 'Unassigned' }} · revision {{ observed.revision }} · status {{ observed.status }}.</p>
              @for (event of assignmentObservedActivity(); track event.id) {
                <p>{{ eventLabel(event.kind) }} · {{ event.actorDisplayName }} · {{ event.assigneeDisplayName ?? 'Unassigned' }} · revision {{ event.taskRevision }}</p>
              }
              @if (observed.revision > selected.revision) {
                <button matButton (click)="acknowledgeAssignmentObservation()" [disabled]="assignmentBusy()">Acknowledge observed owner and refresh</button>
              } @else if (canReviewAssignmentRetry()) {
                <p>Original requested owner: {{ assignmentRetryOwner() }} · expected revision {{ selected.revision }}.</p>
                <label><input type="checkbox" [checked]="assignmentReviewed()" (change)="assignmentReviewed.set($any($event.target).checked)" /> I reviewed the exact original task, revision and requested owner and agree to retry.</label>
                <button matButton (click)="retryAssignment()" [disabled]="!canRetryAssignment()">Retry the exact original assignment</button>
              }
            }
          } @else if (assignmentOptions().length) {
            <label [attr.for]="'task-assignee-' + selected.taskId">New owner
              <select [id]="'task-assignee-' + selected.taskId" [value]="assignmentUserId()" (change)="selectAssignmentOwner($any($event.target).value)">
                <option value="">Choose eligible staff</option>
                @for (option of assignmentOptions(); track option.userId) { <option [value]="option.userId">{{ option.displayName }}</option> }
              </select>
            </label>
            @if (selectedAssignmentOwner(); as newOwner) {
              <p>Confirm {{ selected.assigneeUserId ? 'reassign' : 'assign' }} <strong>{{ selected.title }}</strong> in engagement
                <strong>{{ selected.engagementId }}</strong> from <strong>{{ selected.assigneeUserId ? (selected.assigneeDisplayName ?? selected.assigneeUserId) : 'Unassigned' }}</strong>
                to <strong>{{ newOwner.displayName }}</strong>.</p>
              <label><input type="checkbox" [checked]="assignmentReviewed()" (change)="assignmentReviewed.set($any($event.target).checked)" /> I reviewed this exact task, engagement, current owner, and new owner.</label>
              <button matButton="filled" [disabled]="assignmentBusy() || !assignmentReviewed() || newOwner.userId === selected.assigneeUserId"
                (click)="saveAssignment()">Confirm assignment</button>
            }
          } @else if (!assignmentLoading() && !assignmentError()) { <p>No eligible active staff members are available for this engagement.</p> }
          <button matButton (click)="closeAssignment()" [disabled]="assignmentBusy() || assignmentUncertain()">Cancel</button>
        </section>
      }
      @if (assignmentMessage()) { <p [attr.role]="assignmentFailed() ? 'alert' : 'status'">{{ assignmentMessage() }}</p> }
      <p class="queue-note">Waiting for client is derived from the linked PBC request. Under review and Changes required follow the exact current procedure result; task completion never changes audit review or approval state.</p>
      @if (result.hasMore && result.nextCursor) {
        <button matButton (click)="nextPage(result.nextCursor)">Next tasks</button>
      }
    }
    @if (activityTask(); as selected) {
      <section class="activity-panel" aria-labelledby="task-activity-heading">
        <h2 id="task-activity-heading" tabindex="-1">Task activity</h2>
        <p>{{ selected.title }} · Operational Task · UTC date range {{ activity.data()?.from ?? 'loading' }} to {{ activity.data()?.to ?? 'loading' }}</p>
        <audit-state [loading]="activity.loading()" [error]="activity.error()" label="task activity" />
        @if (activity.data(); as page) {
          <ol>
            @for (event of page.events; track event.id) {
              <li><strong>{{ eventLabel(event.kind) }}</strong> · {{ event.sourceLabel }}
                <small>By {{ event.actorDisplayName }}@if (event.assigneeDisplayName) { · assigned to {{ event.assigneeDisplayName }}} · revision {{ event.taskRevision }}</small>
                @if (event.kind === 'TASK_DUE_DATE_CHANGED') {
                  <small>{{ event.previousDueDate ?? 'No due date' }} → {{ event.newDueDate ?? 'No due date' }}</small>
                }
                @if (event.kind === 'TASK_CANCELLED') { <small>Cancellation reason: {{ event.reason }}</small> }
                @if (event.kind === 'TASK_REOPENED') { <small>Reopening reason: {{ event.reason }}</small> }
                @if (event.kind === 'TASK_SUSPENDED') { <small>Hold reason: {{ event.reason }}</small> }
                @if (event.kind === 'TASK_RESUMED') { <small>Resumption reason: {{ event.reason }}</small> }
                <time [attr.datetime]="event.occurredAt">{{ event.occurredAt }}</time>
              </li>
            } @empty {
              <li>No task activity in this date range.</li>
            }
          </ol>
          @if (page.hasMore && page.nextCursor) {
            <button matButton (click)="nextActivityPage(page.nextCursor)">Older activity</button>
          }
        }
      </section>
    }
    <audit-command-message [message]="actionMessage()" [failed]="actionFailed()" />
    @if (registryError()) { <p role="alert">{{ registryError() }} <button matButton (click)="loadRegistry()">Retry recovery storage</button></p> }
    @if (reconciliation(); as pending) { @if (pending.sameSession) {
      <p>Recovery storage needs reconciliation. Check the current task before continuing.
        <button matButton (click)="finishReconciliation()" [disabled]="!actionVerified(pending.item.requestId)">Reconcile recovery storage</button></p>
    } }
    @for (id of unsentIds(); track id) {
      <p>A task action was not sent, but its local recovery reference still needs cleanup.
        <button matButton [disabled]="!actionVerified(id) || !actionObservations()[id]" (click)="clearUnsentReservation(id)">Remove unsent recovery reference</button></p>
    }
    @for (id of definitiveCleanupIds(); track id) {
      <p>A task action response was received, but its local recovery reference still needs cleanup.
        <button matButton [disabled]="!actionVerified(id)" (click)="finishDefinitiveCleanup(id)">Finish recovery reference cleanup</button></p>
    }
    @if (hasUnverifiedActions()) {
      <p>Retained task actions require a fresh authorized task read before they can be displayed.
        <button matButton (click)="verifyRetainedActions()" [disabled]="verifyingRetained()">Check retained task actions</button></p>
      @if (retainedError()) { <p role="alert">{{ retainedError() }}</p> }
    }
    @for (pending of visibleActions(); track pending.requestId) {
      <section class="activity-panel" aria-label="Unconfirmed task action">
        <p>The {{ actionLabel(pending.action) }} outcome for task {{ pending.taskId }} is unconfirmed. No action will be resent.</p>
        @if (pending.action === 'changeDueDate') { <p>Requested due date: {{ pending.requestedDueDate ?? 'none' }}.</p> }
        @if (pending.action === 'assign' || pending.action === 'reassign') { <p>Requested assignee: {{ pending.requestedAssigneeId }}.</p> }
        @if (actionInFlight(pending.requestId)) { <p role="status">The request is in progress.</p> }
        <button matButton (click)="observeAction(pending.requestId)" [disabled]="actionInFlight(pending.requestId) || observationLoading(pending.requestId)">Check current task</button>
        @if (observationErrors()[pending.requestId]; as error) { <p role="alert">{{ error }}</p> }
        @if (actionObservations()[pending.requestId]; as observed) {
          <p>Current status: {{ observed.row.status }}; revision: {{ observed.row.revision }}; due date: {{ observed.row.dueDate ?? 'none' }}.</p>
          @if (observed.row.revision > pending.expectedRevision) {
            <p>The revision advanced; this observation does not identify which action changed it.</p>
            <button matButton (click)="acknowledgeAction(pending.requestId)" [disabled]="actionInFlight(pending.requestId)">Acknowledge current task</button>
          } @else { <p>The original revision is still current. The previous request may still be in flight, so its outcome remains unresolved.</p> }
        }
      </section>
    }
    }
    @if (evidenceTask(); as selected) {
      @if (taskAccessCurrent() && !evidencePanel()?.authorizedWorkspace()) { <p role="status">Checking prerequisites in the current scope, or they are unavailable.</p><button matButton [disabled]="evidencePanel()?.busy()" (click)="evidenceTask.set(null)">Close prerequisites</button> }
      <div [hidden]="!evidencePanel()?.authorizedWorkspace() || evidencePanel()?.engagementId() !== selected.engagementId || evidencePanel()?.taskId() !== selected.taskId">
        <audit-workflow-task-evidence #queueEvidence [engagementId]="selected.engagementId" [taskId]="selected.taskId" [title]="selected.title"
          [accessCurrent]="evidenceAccessCurrent" [accessRevision]="availability.revision" (changed)="refreshTaskWorkspaces(); refreshActivity()" />
        <button matButton [disabled]="evidencePanel()?.busy()" (click)="evidenceTask.set(null)">Close prerequisites</button>
      </div>
    }
    @if (orderingTask(); as selected) {
      @if (taskAccessCurrent() && !orderingPanel()?.authorizedWorkspace()) { <p role="status">Checking task ordering in the current scope, or it is unavailable.</p><button matButton [disabled]="orderingPanel()?.busy()" (click)="orderingTask.set(null)">Close task ordering</button> }
      <div [hidden]="!orderingPanel()?.authorizedWorkspace() || orderingPanel()?.engagementId() !== selected.engagementId || orderingPanel()?.taskId() !== selected.taskId">
        <audit-workflow-task-dependencies #queueOrdering [engagementId]="selected.engagementId" [taskId]="selected.taskId" [title]="selected.title"
          [accessCurrent]="orderingAccessCurrent" [accessRevision]="availability.revision" (changed)="refreshTaskWorkspaces(); refreshActivity()" />
        <button matButton [disabled]="orderingPanel()?.busy()" (click)="orderingTask.set(null)">Close task ordering</button>
      </div>
    }
    @if (applicabilityTask(); as selected) {
      @if (taskAccessCurrent() && !applicabilityPanel()?.authorizedWorkspace()) { <p role="status">Checking applicability in the current scope, or it is unavailable.</p><button matButton [disabled]="applicabilityPanel()?.busy()" (click)="applicabilityTask.set(null)">Close conditional applicability</button> }
      <div [hidden]="!applicabilityPanel()?.authorizedWorkspace() || applicabilityPanel()?.engagementId() !== selected.engagementId || applicabilityPanel()?.taskId() !== selected.taskId">
        <audit-workflow-task-applicability #queueApplicability [engagementId]="selected.engagementId" [taskId]="selected.taskId" [title]="selected.title"
          [accessCurrent]="applicabilityAccessCurrent" [accessRevision]="availability.revision" (changed)="refreshTaskWorkspaces(); refreshActivity()" />
        <button matButton [disabled]="applicabilityPanel()?.busy()" (click)="applicabilityTask.set(null)">Close conditional applicability</button>
      </div>
    }
    @if (cancellationTask(); as selected) {
      <div [hidden]="!cancellationAccessCurrent()">
        <audit-workflow-task-cancellation [task]="selected" [action]="cancellationAction()" [readable]="cancellationAccessCurrent()"
          [accessCurrent]="cancellationAccessCurrent" [accessRevision]="availability.revision" [recoveryRegistry]="actionRegistry"
          (registryChanged)="cancellationRegistryChanged()" (scopeCheckFailed)="cancellationScopeFailed()" (changed)="cancellationChanged()"
          (reviewActivity)="reviewCancellationActivity(selected)" (refreshRequested)="refreshCancellationQueue()"
          (closeRequested)="closeCancellation()" />
      </div>
    }
    @if (receiptRecovery(); as selected) {
      <div [hidden]="!receiptRecoveryAccessCurrent()">
        @switch (selected.candidate.kind) {
          @case ('evidence') {
            <audit-workflow-task-evidence #receiptEvidence [engagementId]="selected.candidate.engagementId" [taskId]="selected.candidate.taskId" [title]="'Saved task receipt'"
              [accessCurrent]="receiptRecoveryAccessCurrent" [accessRevision]="availability.revision" (changed)="savedReceiptConfirmed()" />
          }
          @case ('ordering') {
            <audit-workflow-task-dependencies #receiptOrdering [engagementId]="selected.candidate.engagementId" [taskId]="selected.candidate.taskId" [title]="'Saved task receipt'"
              [accessCurrent]="receiptRecoveryAccessCurrent" [accessRevision]="availability.revision" (changed)="savedReceiptConfirmed()" />
          }
          @case ('applicability') {
            <audit-workflow-task-applicability #receiptApplicability [engagementId]="selected.candidate.engagementId" [taskId]="selected.candidate.taskId" [title]="'Saved task receipt'"
              [accessCurrent]="receiptRecoveryAccessCurrent" [accessRevision]="availability.revision" (changed)="savedReceiptConfirmed()" />
          }
        }
        <button matButton (click)="closeSavedReceipt()">Close saved receipt</button>
      </div>
    }
  `,
  styleUrl: './work-queue.scss',
})
export class WorkflowWorkQueue {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly availability = inject(WorkspaceAvailability);
  readonly taskRefused = signal(false);
  readonly reviewRefused = signal(false);
  readonly taskAccessCurrent = () => !this.taskRefused() && this.availability.current()?.tasks === 'available';
  readonly reviewAccessCurrent = () => !this.reviewRefused() && this.availability.current()?.reviews === 'available';
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly hostElement = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly renderInjector = inject(Injector);
  private readonly cancellationPanel = viewChild(WorkflowTaskCancellationPanel);
  readonly evidencePanel = viewChild<WorkflowTaskEvidencePanel>('queueEvidence');
  readonly orderingPanel = viewChild<WorkflowTaskDependenciesPanel>('queueOrdering');
  readonly applicabilityPanel = viewChild<WorkflowTaskApplicabilityPanel>('queueApplicability');
  private readonly receiptEvidencePanel = viewChild<WorkflowTaskEvidencePanel>('receiptEvidence');
  private readonly receiptOrderingPanel = viewChild<WorkflowTaskDependenciesPanel>('receiptOrdering');
  private readonly receiptApplicabilityPanel = viewChild<WorkflowTaskApplicabilityPanel>('receiptApplicability');
  readonly ownerFilter = signal<QueueOwner>('ALL');
  readonly views = queueViews;
  readonly displays = QUEUE_DISPLAYS;
  /** How the loaded page is arranged. It never changes which tasks are requested or shown. */
  readonly display = signal<QueueDisplay>('LIST');
  readonly blockedPrerequisites = hasBlockingPrerequisites;
  readonly board = boardColumns;
  readonly calendar = queueCalendar;
  readonly grouped = groupedTasks;
  readonly selectedView = signal<QueueView>('MY_TASKS');
  readonly queueLocationValid = signal(false);
  readonly queueLocationError = signal('');
  private queueLocationNavigation = 0;
  private queueLocationPending = false;
  readonly reminderTaskSelection = signal<ReminderTaskSelection | null>(null);
  readonly reminderTaskLinkError = signal('');
  readonly activityTask = signal<{ engagementId: string; taskId: string; title: string } | null>(null);
  readonly orderingTask = signal<{ engagementId: string; taskId: string; title: string } | null>(null);
  readonly evidenceTask = signal<{ engagementId: string; taskId: string; title: string } | null>(null);
  readonly applicabilityTask = signal<{ engagementId: string; taskId: string; title: string } | null>(null);
  readonly cancellationTask = signal<QueueTask | null>(null);
  readonly cancellationAction = signal<WorkflowTaskRecoveryAction>('cancel');
  readonly assignmentTask = signal<QueueTask | null>(null);
  readonly assignmentOptions = signal<{ userId: string; displayName: string }[]>([]);
  readonly assignmentUserId = signal('');
  readonly assignmentReviewed = signal(false);
  readonly assignmentBusy = signal(false);
  readonly assignmentLoading = signal(false);
  readonly assignmentError = signal('');
  readonly assignmentMessage = signal('');
  readonly assignmentFailed = signal(false);
  readonly assignmentUncertain = signal(false);
  readonly assignmentObservation = signal<AssignmentObservation | null>(null);
  readonly assignmentObservedActivity = signal<ReturnType<typeof decodeActivity>['events']>([]);
  private assignmentIntent: AssignmentIntent | null = null;
  private assignmentTicket: WorkQueueDispatchTicket | null = null;
  private assignmentObservationOwner = '';
  private assignmentObservationRevision = -1;
  private assignmentRequest = 0;
  private assignmentDestroyed = false;
  private cancellationTrigger: { taskId: string; engagementId: string; action: WorkflowTaskRecoveryAction; element: HTMLButtonElement } | null = null;
  private focusInteraction = 0;
  private workQueueDestroyed = false;
  private readonly cursor = signal<{ dueDate: string | null; taskId: string } | null>(null);
  private readonly reviewCursor = signal<{ submittedAt: string; resultId: string } | null>(null);
  private readonly activityCursor = signal<{ occurredAt: string; activityId: string } | null>(null);
  readonly queue = this.api.resource(() => {
    if (!this.taskAccessCurrent() || !this.queueLocationValid()) return null;
    const params = new URLSearchParams({ view: this.selectedView(), pageSize: '50', includeSummary: 'true' });
    if (this.selectedView() !== 'MY_TASKS') {
      if (this.ownerFilter() === 'ME') {
        const user = this.session.current()?.userId;
        if (!user) return null;
        params.set('assigneeUserId', user);
      } else if (this.ownerFilter() === 'UNASSIGNED') params.set('unassignedOnly', 'true');
    }
    const after = this.cursor();
    if (after) {
      if (after.dueDate) params.set('afterDueDate', after.dueDate);
      params.set('afterTaskId', after.taskId);
    }
    return `/api/ui/work/tasks?${params.toString()}`;
  }, decodeQueue, 'The work queue is unavailable in your current scope or is not enabled on this server.',
    undefined, () => this.refuseSection('tasks'));
  readonly reminderTask = this.api.resource(() => {
    const selection = this.reminderTaskSelection();
    return selection && this.taskAccessCurrent()
      ? `/api/ui/engagements/${selection.engagementId}/workflow/tasks?taskId=${encodeURIComponent(selection.taskId)}`
      : null;
  }, (value, path) => decodeReminderTaskWorkspace(value, path, this.reminderTaskSelection()),
  'The selected task is unavailable in your current authorized scope.');
  readonly reviews = this.api.resource(() => {
    if (!this.reviewAccessCurrent()) return null;
    const params = new URLSearchParams({ pageSize: '50' });
    const after = this.reviewCursor();
    if (after) {
      params.set('afterSubmittedAt', after.submittedAt);
      params.set('afterResultId', after.resultId);
    }
    return `/api/ui/work/reviews?${params.toString()}`;
  }, decodeReviewInbox, 'The procedure review inbox is unavailable in your current reviewer scope or is not enabled on this server.',
    undefined, () => this.refuseSection('reviews'));
  readonly activity = this.api.resource(() => {
    const selected = this.activityTask();
    if (!selected || !this.taskAccessCurrent()) return null;
    const params = new URLSearchParams({ pageSize: '50' });
    const after = this.activityCursor();
    if (after) {
      params.set('afterOccurredAt', after.occurredAt);
      params.set('afterActivityId', after.activityId);
    }
    return `/api/ui/engagements/${selected.engagementId}/workflow/tasks/${selected.taskId}/activity?${params.toString()}`;
  }, decodeActivity, 'Task activity is unavailable in your current authorized scope.');
  readonly actionRegistry = new WorkQueueActionRegistry(() => this.registryOwner());
  private readonly receiptDiscovery = new WorkQueueReceiptDiscovery(() => this.registryOwner());
  private receiptCandidates: readonly WorkQueueReceiptCandidate[] = [];
  private receiptOffset = 0;
  private receiptSequence = 0;
  private readonly verifiedReceipts = signal<VerifiedReceipt[]>([]);
  readonly receiptRecovery = signal<VerifiedReceipt | null>(null);
  readonly receiptChecking = signal(false);
  readonly receiptMessage = signal('');
  readonly registryRecords = signal<WorkQueuePendingAction[]>([]);
  readonly registryError = signal('');
  readonly registryReady = signal(false);
  readonly reconciliation = signal<WorkQueueReconciliation | null>(null);
  readonly priorFenceMessage = signal('');
  readonly actionMessage = signal('');
  readonly actionFailed = signal(false);
  readonly verifyingRetained = signal(false);
  readonly retainedError = signal('');
  private readonly runningActions = signal<string[]>([]);
  readonly unsentIds = signal<string[]>([]);
  private readonly unsentTickets = new Map<string, WorkQueueDispatchTicket>();
  readonly definitiveCleanupIds = signal<string[]>([]);
  private readonly definitiveCleanupTickets = new Map<string, { ticket: WorkQueueDispatchTicket; success: boolean; message: string }>();
  private readonly verifiedActions = signal<Record<string, { owner: string; revision: number }>>({});
  private readonly seenCancellationRequests = new Set<string>();
  readonly actionObservations = signal<Record<string, { row: ReturnType<typeof decodeWorkflowTaskWorkspace>['tasks'][number]; owner: string; revision: number }>>({});
  readonly observationErrors = signal<Record<string, string>>({});
  private readonly loadingObservations = signal<string[]>([]);
  private observationSequence = 0;
  private readonly observationRequests = new Map<string, number>();

  constructor() {
    const routeSubscription = this.route.queryParamMap.subscribe(params => {
      // Query parameters may be emitted during activation, before navigation commits.
      if (this.router.currentNavigation()) return;
      ++this.queueLocationNavigation;
      this.queueLocationPending = false;
      this.readQueueLocation(params);
      this.readReminderTaskSelection(params);
    });
    const navigationSubscription = this.router.events.subscribe(event => {
      if (!(event instanceof NavigationEnd)) return;
      ++this.queueLocationNavigation;
      this.queueLocationPending = false;
      this.readQueueLocation(this.route.snapshot.queryParamMap);
      this.readReminderTaskSelection(this.route.snapshot.queryParamMap);
    });
    let sessionOwner = this.sessionOwnerKey();
    let availabilityRevision = this.availability.revision();
    this.loadRegistry();
    effect(() => {
      const owner = this.sessionOwnerKey();
      if (owner === sessionOwner) return;
      sessionOwner = owner;
      untracked(() => {
        this.cursor.set(null); this.reviewCursor.set(null); this.activityCursor.set(null);
        this.reminderTaskSelection.set(null); this.reminderTaskLinkError.set('');
        this.evidenceTask.set(null); this.orderingTask.set(null); this.activityTask.set(null);
        this.applicabilityTask.set(null);
        this.cancellationTask.set(null); this.cancellationAction.set('cancel'); this.cancellationTrigger = null; this.resetAssignment();
        this.taskRefused.set(false); this.reviewRefused.set(false);
        this.runningActions.set([]); this.clearActionObservations(); this.actionMessage.set(''); this.actionFailed.set(false);
        this.priorFenceMessage.set('');
        this.unsentTickets.clear(); this.unsentIds.set([]);
        this.definitiveCleanupTickets.clear(); this.definitiveCleanupIds.set([]);
        this.clearSavedReceiptDiscovery();
        this.loadRegistry();
      });
    });
    effect(() => {
      const revision = this.availability.revision();
      const tasksAvailable = this.taskAccessCurrent();
      untracked(() => {
        if (revision !== availabilityRevision) {
          availabilityRevision = revision;
          this.assignmentMessage.set(''); this.assignmentFailed.set(false);
          this.clearSavedReceiptDiscovery();
          const cancellation = this.cancellationPanel();
          cancellation?.invalidateAvailability();
          if (cancellation && !cancellation.hasUnresolvedOutcome() && !cancellation.activeRegistryRequest()) {
            this.cancellationTask.set(null); this.cancellationAction.set('cancel'); this.cancellationTrigger = null;
          }
          this.clearActionObservations();
          if (this.assignmentIntent && (this.assignmentBusy() || this.assignmentUncertain())) {
            ++this.assignmentRequest;
            this.assignmentBusy.set(false); this.assignmentLoading.set(false);
            this.assignmentUncertain.set(true); this.assignmentReviewed.set(false);
            this.assignmentObservation.set(null); this.assignmentObservedActivity.set([]);
            this.assignmentMessage.set('Access changed while the assignment was unresolved. Check the current task again before continuing.');
          } else if (this.assignmentTask()) this.resetAssignment();
        }
        this.cursor.set(null); this.reviewCursor.set(null); this.activityCursor.set(null);
        if (tasksAvailable) this.readReminderTaskSelection(this.route.snapshot.queryParamMap);
        else { this.reminderTaskSelection.set(null); this.reminderTaskLinkError.set(''); }
        if (!tasksAvailable) { this.clearActionObservations(); this.clearSettledPanels(); this.clearSavedReceiptDiscovery(); }
        this.activityTask.set(null);
        // Assignment and cancellation may have an unconfirmed command outcome. Retain the
        // session-bound recovery state while availability is rechecked; hide it until allowed.
      });
    });
    inject(DestroyRef).onDestroy(() => {
      routeSubscription.unsubscribe();
      navigationSubscription.unsubscribe();
      this.assignmentDestroyed = true; this.workQueueDestroyed = true; ++this.assignmentRequest;
    });
  }

  @HostListener('document:pointerdown')
  @HostListener('document:keydown')
  recordFocusInteraction(): void { this.focusInteraction++; }

  private refuseSection(section: 'tasks' | 'reviews'): void {
    const refused = section === 'tasks' ? this.taskRefused : this.reviewRefused;
    if (refused()) return;
    refused.set(true);
    if (this.availability.state() !== 'loading') {
      this.availability.invalidate();
      this.availability.refresh();
    }
  }

  retrySection(section: 'tasks' | 'reviews'): void {
    this.availability.invalidate();
    (section === 'tasks' ? this.taskRefused : this.reviewRefused).set(false);
    this.availability.refresh();
  }

  labelFor(value: string): string {
    return this.views.find(x => x.key === value)?.label ?? 'Work';
  }

  scrollWorkTable(event: KeyboardEvent): void {
    if (event.target !== event.currentTarget || !(event.currentTarget instanceof HTMLElement)) return;
    const region = event.currentTarget;
    const maxScroll = region.scrollWidth - region.clientWidth;
    if (maxScroll <= 0) return;
    const step = Math.max(40, Math.floor(region.clientWidth * 0.8));
    const nextScroll = event.key === 'ArrowRight' ? region.scrollLeft + step
      : event.key === 'ArrowLeft' ? region.scrollLeft - step
        : event.key === 'Home' ? 0
          : event.key === 'End' ? maxScroll : null;
    if (nextScroll === null) return;
    event.preventDefault();
    region.scrollLeft = Math.max(0, Math.min(maxScroll, nextScroll));
  }

  refreshActivity(): void { if (this.taskAccessCurrent()) this.activity.reload(); }

  refreshTaskWorkspaces(): void {
    if (!this.taskAccessCurrent()) return;
    if (this.queueLocationValid()) this.queue.reload();
    this.refreshReminderTask();
  }

  private refreshReminderTask(): void {
    if (this.taskAccessCurrent() && this.reminderTaskSelection()) this.reminderTask.reload();
  }

  openLinkedTaskPanel(panel: 'activity' | 'evidence' | 'ordering'): void {
    const selection = this.reminderTaskSelection(), selected = this.currentReminderTask();
    if (!this.taskAccessCurrent() || !selection || !selected || this.reminderTask.loading() || this.reminderTask.error() ||
        selected.id.toLowerCase() !== selection.taskId) return;
    const task = { engagementId: selection.engagementId, taskId: selection.taskId, title: selected.title };
    if (panel === 'activity') this.showActivity(task.engagementId, task.taskId, task.title);
    else if (panel === 'evidence') this.selectEvidence(task);
    else this.selectOrdering(task);
  }

  private clearLinkedTaskPanels(): void {
    if (!this.reminderTaskSelection()) return;
    this.clearSettledPanels();
    this.activityTask.set(null);
    this.activityCursor.set(null);
  }

  currentReminderTask(): SelectedReminderTask | null {
    const selected = this.reminderTaskSelection();
    const task = this.reminderTask.data();
    return selected && task && task.engagementId === selected.engagementId &&
      task.id.toLowerCase() === selected.taskId ? task : null;
  }

  reminderQueueTask(): QueueTask | null {
    const selected = this.reminderTaskSelection();
    const currentTask = this.currentReminderTask();
    if (!selected || !currentTask || !this.queueLocationValid()) return null;
    const sameTaskInQueue = this.queue.data()?.tasks.find(task =>
      task.engagementId.toLowerCase() === selected.engagementId && task.taskId.toLowerCase() === selected.taskId);
    if (sameTaskInQueue) {
      return sameTaskInQueue.taskStatus === currentTask.status && sameTaskInQueue.revision === currentTask.revision
        ? sameTaskInQueue
        : null;
    }
    const hasAnyAction = currentTask.canComplete || currentTask.canChangeDueDate || currentTask.canAssign ||
      currentTask.canCancel || currentTask.canReopen || currentTask.canSuspend || currentTask.canResume;
    if (!hasAnyAction) return null;
    return {
      taskId: currentTask.id,
      clientId: selected.engagementId,
      clientName: '',
      engagementId: selected.engagementId,
      engagementServiceRoute: '',
      engagementPeriod: '',
      title: currentTask.title,
      phase: currentTask.phase,
      ordinal: currentTask.ordinal,
      taskStatus: currentTask.status,
      assigneeUserId: currentTask.assigneeUserId,
      assigneeDisplayName: currentTask.assigneeDisplayName,
      dueDate: currentTask.dueDate,
      mappingVersionId: null,
      fsliCode: null,
      unmetTaskDependencies: currentTask.unmetTaskDependencies,
      unmetEvidencePrerequisites: currentTask.unmetEvidencePrerequisites,
      canComplete: currentTask.canComplete,
      canChangeDueDate: currentTask.canChangeDueDate,
      canAssign: currentTask.canAssign,
      canCancel: currentTask.canCancel,
      canReopen: currentTask.canReopen,
      revision: currentTask.revision,
      sourceKey: '',
      templateRevisionId: null,
      templateStepId: null,
      requiredTask: null,
      taskGroupName: null,
      applicabilityCondition: null,
      pbcRequestId: null,
      pbcState: null,
      reviewState: null,
      reviewComment: null,
      onHold: currentTask.onHold,
      holdReason: null,
      canSuspend: currentTask.canSuspend,
      canResume: currentTask.canResume,
      manualRequiredTask: null,
    };
  }

  reminderSourceTarget(source: ReminderSource): {
    commands: string[]; queryParams: Record<string, string> | null; label: string;
  } | null {
    const selection = this.reminderTaskSelection();
    if (!selection || !reminderSourceHrefAllowed(selection.engagementId, source)) return null;
    return source.sourceKind === 'TASK_DUE'
      ? { commands: ['/app/work'], queryParams: { engagementId: selection.engagementId, taskId: source.sourceId }, label: 'Open originating workflow task' }
      : { commands: ['/app/engagements', selection.engagementId, 'pbc'], queryParams: null, label: 'Open PBC workspace' };
  }

  private readReminderTaskSelection(params: ParamMap): void {
    const engagementIds = params.getAll('engagementId');
    const taskIds = params.getAll('taskId');
    if (engagementIds.length === 0 && taskIds.length === 0) {
      this.clearLinkedTaskPanels();
      this.reminderTaskLinkError.set('');
      this.reminderTaskSelection.set(null);
      return;
    }
    if (engagementIds.length !== 1 || taskIds.length !== 1) {
      this.clearLinkedTaskPanels();
      this.reminderTaskSelection.set(null);
      this.reminderTaskLinkError.set('This task link is invalid. Open the reminder again.');
      return;
    }
    try {
      const next = {
        engagementId: guid(engagementIds[0], 'engagementId').toLowerCase(),
        taskId: guid(taskIds[0], 'taskId').toLowerCase(),
      };
      this.reminderTaskLinkError.set('');
      const current = this.reminderTaskSelection();
      if (current?.engagementId === next.engagementId && current.taskId === next.taskId) return;
      this.clearLinkedTaskPanels();
      this.reminderTaskSelection.set(next);
      this.focusReminderTaskHeading(next);
    } catch {
      this.clearLinkedTaskPanels();
      this.reminderTaskSelection.set(null);
      this.reminderTaskLinkError.set('This task link is invalid. Open the reminder again.');
    }
  }

  private readQueueLocation(params: ParamMap): void {
    const location = parseWorkQueueLocation(params);
    if (!location) {
      this.queueLocationValid.set(false);
      this.queueLocationError.set(hasUnsupportedQueueContext(params)
        ? 'Client and engagement queue filters are not supported yet. Reset queue filters to open the default view.'
        : 'This queue link has invalid filters. Reset them to open the default view.');
      this.cursor.set(null);
      this.clearSettledPanels(); this.clearSettledCancellation();
      return;
    }
    this.applyQueueLocation(location);
  }

  private applyQueueLocation(location: WorkQueueLocation): void {
    if (location.view !== this.selectedView() || location.owner !== this.ownerFilter()) {
      this.cursor.set(null);
      this.clearSettledPanels(); this.clearSettledCancellation();
    }
    this.selectedView.set(location.view);
    this.ownerFilter.set(location.owner);
    this.display.set(location.display);
    this.queueLocationError.set('');
    this.queueLocationValid.set(true);
  }

  private navigateQueueLocation(location: WorkQueueLocation, resetContext = false): void {
    if (!this.queueLocationPending && this.queueLocationValid() && this.selectedView() === location.view &&
        this.ownerFilter() === location.owner && this.display() === location.display) return;
    const navigation = ++this.queueLocationNavigation;
    this.queueLocationPending = true;
    void this.router.navigate([], { relativeTo: this.route, queryParamsHandling: 'merge', preserveFragment: true,
      queryParams: { queueView: location.view, queueAssignment: location.owner, queueDisplay: location.display,
        ...(resetContext ? { queueClientId: null, queueEngagementId: null } : {}) } })
      .then(success => {
        if (this.workQueueDestroyed || navigation !== this.queueLocationNavigation) return;
        this.queueLocationPending = false;
        if (success) return;
        const actual = parseWorkQueueLocation(this.route.snapshot.queryParamMap);
        if (actual && actual.view === location.view && actual.owner === location.owner && actual.display === location.display) {
          this.applyQueueLocation(actual);
          return;
        }
        this.queueLocationValid.set(false);
        this.queueLocationError.set('The queue link could not be updated. Reset the filters to try again.');
        this.cursor.set(null);
      }).catch(() => {
        if (this.workQueueDestroyed || navigation !== this.queueLocationNavigation) return;
        this.queueLocationPending = false;
        this.queueLocationValid.set(false);
        this.queueLocationError.set('The queue link could not be updated. Reset the filters to try again.');
        this.cursor.set(null);
      });
  }

  resetQueueLocation(): void {
    this.navigateQueueLocation({ view: 'MY_TASKS', owner: 'ALL', display: 'LIST' }, true);
  }

  private focusReminderTaskHeading(selection: ReminderTaskSelection): void {
    const root = this.hostElement.nativeElement;
    const document = root.ownerDocument;
    const active = document.activeElement;
    if (active !== document.body && !root.contains(active)) return;
    const owner = this.sessionOwnerKey();
    const interaction = this.focusInteraction;
    afterNextRender(() => {
      const current = this.reminderTaskSelection();
      if (this.workQueueDestroyed || owner !== this.sessionOwnerKey() || interaction !== this.focusInteraction ||
          current?.engagementId !== selection.engagementId || current.taskId !== selection.taskId ||
          (document.activeElement !== active && document.activeElement !== document.body)) return;
      root.querySelector<HTMLElement>('#reminder-task-heading')?.focus();
    }, { injector: this.renderInjector });
  }

  /** Groups an exact count string for display without converting it to a number. */
  groupCount(value: string): string {
    return value.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  }

  selectView(value: QueueView): void {
    if (!this.queueLocationValid()) return;
    if (this.selectedView() === value && this.ownerFilter() === 'ALL') {
      this.clearSettledPanels(); this.clearSettledCancellation(); this.cursor.set(null);
    }
    this.navigateQueueLocation({ view: value, owner: 'ALL', display: this.display() });
  }

  selectOwnerFilter(value: string): void {
    if (!this.queueLocationValid() || this.selectedView() === 'MY_TASKS' || !['ALL', 'ME', 'UNASSIGNED'].includes(value)) return;
    if (this.ownerFilter() === value) {
      this.clearSettledPanels(); this.clearSettledCancellation(); this.cursor.set(null);
    }
    this.navigateQueueLocation({ view: this.selectedView(), owner: value as QueueOwner, display: this.display() });
  }

  selectDisplay(value: QueueDisplay): void {
    if (!this.queueLocationValid() || !this.displays.some(item => item.key === value)) return;
    this.navigateQueueLocation({ view: this.selectedView(), owner: this.ownerFilter(), display: value });
  }

  nextPage(cursor: { dueDate: string | null; taskId: string }): void {
    this.clearSettledPanels(); this.clearSettledCancellation();
    this.cursor.set(cursor);
  }

  private clearSettledCancellation(): void {
    if (this.cancellationPanel()?.hasUnresolvedOutcome()) return;
    this.cancellationTask.set(null); this.cancellationAction.set('cancel'); this.cancellationTrigger = null;
  }

  evidenceUnresolved(): boolean { const panel = this.evidencePanel(); return !!panel && (panel.busy() || !!panel.pending()); }
  orderingUnresolved(): boolean { const panel = this.orderingPanel(); return !!panel && (panel.busy() || !!panel.pending()); }
  applicabilityUnresolved(): boolean { const panel = this.applicabilityPanel(); return !!panel && (panel.busy() || !!panel.pending()); }
  selectEvidence(task: Pick<QueueTask, 'engagementId' | 'taskId' | 'title'>): void {
    this.loadRegistry();
    if (!this.evidencePanel()?.busy() && this.canReplacePendingPanel(this.evidenceTask(), task, !!this.evidencePanel()?.pending()) &&
        !this.panelConflict(task, 'evidence')) this.evidenceTask.set(task);
  }
  selectOrdering(task: Pick<QueueTask, 'engagementId' | 'taskId' | 'title'>): void {
    this.loadRegistry();
    if (!this.orderingPanel()?.busy() && this.canReplacePendingPanel(this.orderingTask(), task, !!this.orderingPanel()?.pending()) &&
        !this.panelConflict(task, 'ordering')) this.orderingTask.set(task);
  }
  selectApplicability(task: QueueTask): void {
    this.loadRegistry();
    if (!this.applicabilityPanel()?.busy() && this.canReplacePendingPanel(this.applicabilityTask(), task, !!this.applicabilityPanel()?.pending()) &&
        !this.panelConflict(task, 'applicability')) this.applicabilityTask.set(task);
  }
  private canReplacePendingPanel(current: { engagementId: string; taskId: string } | null,
    next: { engagementId: string; taskId: string }, pending: boolean): boolean {
    if (!pending || current?.engagementId.toLowerCase() === next.engagementId.toLowerCase() &&
        current?.taskId.toLowerCase() === next.taskId.toLowerCase()) return true;
    this.actionFailed.set(true);
    this.actionMessage.set('Resolve or explicitly close the pending task workspace before opening another task. Its recovery reference is retained.');
    return false;
  }
  private clearSettledPanels(): void {
    if (!this.evidenceUnresolved()) this.evidenceTask.set(null);
    if (!this.orderingUnresolved()) this.orderingTask.set(null);
    if (!this.applicabilityUnresolved()) this.applicabilityTask.set(null);
  }

  nextReviewPage(cursor: { submittedAt: string; resultId: string }): void {
    this.reviewCursor.set(cursor);
  }

  showActivity(engagementId: string, taskId: string, title: string): void {
    if (this.activityTask()?.taskId === taskId) {
      this.activityTask.set(null);
      return;
    }
    this.activityCursor.set(null);
    this.activityTask.set({ engagementId, taskId, title });
  }

  nextActivityPage(cursor: { occurredAt: string; activityId: string }): void {
    this.activityCursor.set(cursor);
  }

  eventLabel(kind: string): string {
    return ({ TASK_CREATED: 'Task created', TASK_ASSIGNED: 'Task assigned',
      TASK_REASSIGNED: 'Task reassigned', TASK_COMPLETED: 'Task completed',
      TASK_CANCELLED: 'Task cancelled',
      TASK_REOPENED: 'Task reopened',
      TASK_SUSPENDED: 'Task put on hold', TASK_RESUMED: 'Task resumed',
      TASK_DUE_DATE_CHANGED: 'Due date changed', TASK_EVIDENCE_RULE_CHANGED: 'Evidence prerequisite changed', TASK_DEPENDENCIES_CHANGED: 'Task ordering changed' } as Record<string, string>)[kind] ?? 'Workflow event';
  }

  openCancellation(task: QueueTask, event: MouseEvent): void {
    this.openRecovery(task, 'cancel', event);
  }

  openReopening(task: QueueTask, event: MouseEvent): void {
    if (!this.reopenable(task)) return;
    this.openRecovery(task, 'reopen', event);
  }

  openHold(task: QueueTask, event: MouseEvent): void {
    this.openRecovery(task, task.canResume ? 'resume' : 'suspend', event);
  }

  reopenable(task: QueueTask): boolean { return canReopenTask(task); }

  private openRecovery(task: QueueTask, action: WorkflowTaskRecoveryAction, event: MouseEvent): void {
    this.loadRegistry();
    if (this.cancellationTask() || !this.canRecover(task, action) || this.panelConflict(task, 'cancellation')) return;
    const trigger = event.currentTarget as HTMLButtonElement;
    const owner = this.sessionOwnerKey();
    const interaction = this.focusInteraction;
    this.cancellationTrigger = { taskId: task.taskId, engagementId: task.engagementId, action, element: trigger };
    this.cancellationAction.set(action);
    this.cancellationTask.set(task);
    afterNextRender(() => {
      const document = trigger.ownerDocument;
      if (this.workQueueDestroyed || owner !== this.sessionOwnerKey() || interaction !== this.focusInteraction ||
          this.cancellationTask()?.taskId !== task.taskId || this.cancellationTask()?.engagementId !== task.engagementId ||
          (document.activeElement !== trigger && document.activeElement !== document.body)) return;
      this.cancellationPanel()?.focusReview();
    }, { injector: this.renderInjector });
  }

  private canRecover(task: QueueTask, action: WorkflowTaskRecoveryAction): boolean {
    return action === 'cancel' ? task.canCancel : action === 'reopen' ? canReopenTask(task) :
      action === 'suspend' ? task.canSuspend : task.canResume;
  }

  closeCancellation(): void {
    const selected = this.cancellationTask();
    if (!selected) return;
    const owner = this.sessionOwnerKey();
    const interaction = this.focusInteraction;
    const trigger = this.cancellationTrigger;
    const action = trigger?.action ?? this.cancellationAction();
    const document = this.hostElement.nativeElement.ownerDocument;
    const active = document.activeElement;
    this.cancellationTask.set(null);
    this.cancellationAction.set('cancel');
    this.cancellationTrigger = null;
    afterNextRender(() => {
      if (this.workQueueDestroyed || owner !== this.sessionOwnerKey() || interaction !== this.focusInteraction ||
          (document.activeElement !== active && document.activeElement !== document.body)) return;
      const prefix = action === 'cancel' || action === 'reopen' ? action : 'hold';
      const selector = `button[data-${prefix}-task]`;
      const taskAttribute = `${prefix}Task`;
      const engagementAttribute = `${prefix}Engagement`;
      const current = trigger?.element.isConnected ? trigger.element :
        [...this.hostElement.nativeElement.querySelectorAll<HTMLButtonElement>(selector)]
          .find(button => button.dataset[taskAttribute] === selected.taskId && button.dataset[engagementAttribute] === selected.engagementId);
      const freshFallback = this.hostElement.nativeElement.querySelector<HTMLButtonElement>('.queue-views button[aria-pressed="true"]') ??
        this.hostElement.nativeElement.querySelector<HTMLButtonElement>('.queue-views button');
      // A display change replaces cards with closed native disclosures. Reveal the current action before restoring focus.
      if (current?.isConnected) {
        const disclosure = current.closest<HTMLDetailsElement>('details');
        if (disclosure) disclosure.open = true;
        current.focus();
      } else freshFallback?.focus();
    }, { injector: this.renderInjector });
  }

  cancellationChanged(): void {
    this.refreshTaskWorkspaces();
  }

  reviewCancellationActivity(task: QueueTask): void {
    const owner = this.sessionOwnerKey();
    const interaction = this.focusInteraction;
    const document = this.hostElement.nativeElement.ownerDocument;
    const active = document.activeElement;
    const origin = active instanceof Element && active.closest('audit-workflow-task-cancellation') ? active : null;
    const current = this.activityTask();
    const sameTask = current?.engagementId === task.engagementId && current.taskId === task.taskId;
    const alreadyAtFirstPage = this.activityCursor() === null;
    this.activityCursor.set(null);
    this.activityTask.set({ engagementId: task.engagementId, taskId: task.taskId, title: task.title });
    this.cancellationTask.set(null);
    this.cancellationAction.set('cancel');
    this.cancellationTrigger = null;
    this.refreshTaskWorkspaces();
    if (sameTask && alreadyAtFirstPage) this.refreshActivity();
    afterNextRender(() => {
      if (this.workQueueDestroyed || owner !== this.sessionOwnerKey() || interaction !== this.focusInteraction ||
          !origin || (document.activeElement !== origin && document.activeElement !== document.body) ||
          this.activityTask()?.engagementId !== task.engagementId || this.activityTask()?.taskId !== task.taskId) return;
      this.hostElement.nativeElement.querySelector<HTMLElement>('#task-activity-heading')?.focus();
    }, { injector: this.renderInjector });
  }

  refreshCancellationQueue(): void {
    this.refreshTaskWorkspaces();
  }

  private sessionOwnerKey(): string {
    const actor = this.session.current();
    return [actor?.firmId, actor?.userId, actor?.generation, actor?.staff, this.session.invalidation()].join('|');
  }

  async openAssignment(task: QueueTask): Promise<void> {
    this.loadRegistry();
    if (!this.taskAccessCurrent() || !task.canAssign || this.assignmentBusy() || this.assignmentUncertain() || this.panelConflict(task, 'assignment')) return;
    const scope = this.assignmentScope(), request = ++this.assignmentRequest;
    const accessRevision = this.availability.revision();
    this.assignmentTask.set(task); this.assignmentOptions.set([]); this.assignmentUserId.set('');
    this.assignmentReviewed.set(false); this.assignmentError.set(''); this.assignmentMessage.set('');
    this.assignmentLoading.set(true);
    try {
      const options = await this.api.get(`/api/ui/engagements/${task.engagementId}/workflow/tasks/assignees`, decodeAssigneeOptions);
      if (this.assignmentCurrent(scope, request) && this.taskAccessCurrent() && accessRevision === this.availability.revision()) this.assignmentOptions.set(options);
    } catch (error) {
      if (this.assignmentCurrent(scope, request)) this.assignmentError.set(error instanceof Error ? error.message : 'Eligible staff are unavailable. Refresh the task list.');
    } finally { if (this.assignmentCurrent(scope, request)) this.assignmentLoading.set(false); }
  }

  selectedAssignmentOwner(): { userId: string; displayName: string } | null {
    return this.assignmentOptions().find(x => x.userId === this.assignmentUserId()) ?? null;
  }

  selectAssignmentOwner(userId: string): void {
    if (this.assignmentBusy() || this.assignmentUncertain()) return;
    this.assignmentUserId.set(userId); this.assignmentReviewed.set(false); this.assignmentMessage.set('');
  }

  closeAssignment(): void {
    if (this.assignmentBusy() || this.assignmentUncertain()) return;
    this.resetAssignment();
  }

  private resetAssignment(): void {
    ++this.assignmentRequest;
    this.assignmentTask.set(null); this.assignmentOptions.set([]); this.assignmentUserId.set('');
    this.assignmentReviewed.set(false); this.assignmentError.set('');
    this.assignmentBusy.set(false); this.assignmentLoading.set(false); this.assignmentUncertain.set(false);
    this.assignmentMessage.set(''); this.assignmentFailed.set(false);
    this.assignmentObservation.set(null); this.assignmentObservedActivity.set([]); this.assignmentIntent = null;
    this.assignmentTicket = null; this.assignmentObservationOwner = ''; this.assignmentObservationRevision = -1;
  }

  private assignmentScope(): string {
    const actor = this.session.current();
    return [actor?.firmId, actor?.userId, actor?.generation, actor?.staff, this.session.invalidation()].join('|');
  }

  private assignmentCurrent(scope: string, request: number): boolean {
    return !this.assignmentDestroyed && request === this.assignmentRequest && scope === this.assignmentScope();
  }

  async saveAssignment(): Promise<void> {
    const task = this.assignmentTask(); const owner = this.selectedAssignmentOwner();
    this.loadRegistry();
    if (!this.taskAccessCurrent() || !task || !owner || !task.canAssign || this.panelConflict(task, 'assignment') || !this.assignmentReviewed() || this.assignmentBusy() || this.assignmentUncertain() || owner.userId === task.assigneeUserId) return;
    this.assignmentIntent = { task: { ...task }, owner: { ...owner }, action: task.assigneeUserId ? 'reassign' : 'assign', scope: this.assignmentScope() };
    await this.dispatchAssignment(false);
  }

  private async dispatchAssignment(retry: boolean): Promise<void> {
    const intent = this.assignmentIntent;
    if (!intent || intent.scope !== this.assignmentScope()) return;
    const { task, owner, action, scope } = intent;
    const registryOwner = this.registryOwner();
    if (!registryOwner || retry && (!this.assignmentTicket || !this.canRetryAssignment())) return;
    this.loadRegistry();
    if (this.panelConflict(task, 'assignment', retry ? this.assignmentTicket?.item.requestId ?? null : null)) return;
    const accessRevision = this.availability.revision();
    let ticket = this.assignmentTicket;
    if (!retry) {
      let requestId: string;
      try { requestId = crypto.randomUUID(); } catch { this.assignmentFailed.set(true); this.assignmentMessage.set('A recovery reference could not be created. No assignment was sent.'); return; }
      const reserved = this.actionRegistry.reserveForDispatchRecoverable(registryOwner, { engagementId: task.engagementId, taskId: task.taskId,
        expectedRevision: task.revision, action, requestedDueDate: null, requestedAssigneeId: owner.userId, requestId });
      if (!reserved.ok) { if ('cleanup' in reserved) this.rememberUnsent(reserved.cleanup); this.loadRegistry(); this.assignmentFailed.set(true); this.assignmentMessage.set('The assignment could not be safely reserved. No assignment was sent.'); return; }
      ticket = reserved.value; this.assignmentTicket = ticket; this.loadRegistry();
      if (!this.registryReady() || scope !== this.assignmentScope() || !this.taskAccessCurrent() || accessRevision !== this.availability.revision()) {
        const aborted = this.actionRegistry.abortBeforeDispatch(ticket);
        if (scope === this.assignmentScope()) { if (aborted.ok) this.assignmentTicket = null; else this.rememberUnsent(ticket); this.loadRegistry(); this.assignmentFailed.set(true); this.assignmentMessage.set(aborted.ok ? 'Access changed. No assignment was sent.' : 'No assignment was sent; the recovery reference could not be cleared.'); }
        return;
      }
      this.verifiedActions.update(items => ({ ...items, [ticket!.item.requestId]: { owner: this.sessionOwnerKey(), revision: accessRevision } }));
    }
    if (!ticket) return;
    const send = () => this.api.command(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/${action}`,
      { assigneeUserId: owner.userId, expectedRevision: task.revision });
    const invoked = retry
      ? this.actionRegistry.invokeExactRetry<CommandOutcome>(ticket, () => this.canRetryAssignment() && scope === this.assignmentScope() && accessRevision === this.availability.revision())
      : this.actionRegistry.invokeReserved(ticket, send);
    if (!invoked.ok) {
      const aborted = !retry ? this.actionRegistry.abortBeforeDispatch(ticket) : null;
      if (scope === this.assignmentScope()) { if (aborted?.ok) this.assignmentTicket = null; else if (!retry) this.rememberUnsent(ticket); this.loadRegistry(); this.assignmentFailed.set(true); this.assignmentMessage.set('The assignment was not sent; its recovery reference requires review.'); }
      return;
    }
    const request = ++this.assignmentRequest;
    this.assignmentBusy.set(true); this.assignmentFailed.set(false); this.assignmentMessage.set('');
    this.assignmentObservation.set(null); this.assignmentObservedActivity.set([]);
    let outcome: CommandOutcome;
    try { outcome = await invoked.value; }
    catch {
      if (this.assignmentCurrent(scope, request)) { this.assignmentBusy.set(false); this.assignmentUncertain.set(true); this.assignmentFailed.set(true); this.assignmentMessage.set('The assignment outcome is unconfirmed.'); }
      return;
    }
    if (!this.assignmentCurrent(scope, request)) return;
    if (!this.taskAccessCurrent() || accessRevision !== this.availability.revision()) {
      this.assignmentBusy.set(false); this.assignmentUncertain.set(true); this.assignmentReviewed.set(false);
      this.assignmentMessage.set('Access changed while the assignment was unresolved. Check the current task again before continuing.');
      return;
    }
    this.assignmentBusy.set(false);
    if (outcome.ok && outcome.value === true || !retry && !outcome.ok && !outcome.unknown) {
      const removed = this.actionRegistry.removeDefinitiveTicket(ticket);
      const finished = removed.ok && this.taskAccessCurrent() && scope === this.assignmentScope() && accessRevision === this.availability.revision()
        ? this.actionRegistry.finishDefinitiveTicket(ticket) : null;
      if (!removed.ok || !finished?.ok) { this.rememberDefinitiveCleanup(ticket, outcome.ok, outcome.ok ? 'Task assignment response confirmed.' : 'The assignment was refused. Review the current task.'); this.loadRegistry(); this.assignmentUncertain.set(true); this.assignmentFailed.set(true); this.assignmentMessage.set('The response was received, but the recovery reference could not be cleared.'); return; }
      this.loadRegistry();
    }
    if (outcome.ok && outcome.value === true) {
      this.resetAssignment();
      this.assignmentMessage.set(`Task assigned to ${owner.displayName}.`);
      this.refreshTaskWorkspaces(); this.refreshActivity();
      return;
    }
    this.assignmentFailed.set(true); this.assignmentMessage.set(outcome.ok ? 'The assignment response could not be confirmed.' : outcome.message);
    if (outcome.ok || outcome.unknown || retry) {
      this.assignmentUncertain.set(true); this.assignmentReviewed.set(false);
      this.assignmentMessage.set('The original assignment remains unconfirmed. Check the current task and its activity before retrying or choosing another owner.');
      this.refreshReminderTask();
    } else if (outcome.status === 409 || /stale|revision/i.test(outcome.code)) {
      this.resetAssignment();
      this.assignmentFailed.set(true);
      this.refreshTaskWorkspaces(); this.refreshActivity();
      this.assignmentMessage.set('The task changed before assignment. The queue has been refreshed; reopen it and review the current owner and revision.');
    } else if (outcome.status === 403) {
      this.resetAssignment(); this.assignmentFailed.set(true); this.assignmentMessage.set(outcome.message); this.refreshTaskWorkspaces();
    } else {
      this.assignmentIntent = null; this.assignmentReviewed.set(false);
    }
  }

  async recoverAssignment(): Promise<void> {
    const intent = this.assignmentIntent;
    if (!this.taskAccessCurrent() || !intent || !this.assignmentUncertain() || this.assignmentBusy() || this.assignmentLoading() || intent.scope !== this.assignmentScope()) return;
    const request = ++this.assignmentRequest, { task, scope } = intent;
    const accessRevision = this.availability.revision();
    this.assignmentLoading.set(true); this.assignmentError.set(''); this.assignmentReviewed.set(false);
    this.assignmentObservation.set(null); this.assignmentObservedActivity.set([]);
    try {
      const [workspace, history] = await Promise.all([
        this.api.get(`/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${encodeURIComponent(task.taskId)}`, decodeWorkflowTaskWorkspace),
        this.api.get(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/activity?pageSize=50`, decodeActivity),
      ]);
      if (!this.assignmentCurrent(scope, request)) return;
      if (!this.taskAccessCurrent() || accessRevision !== this.availability.revision()) {
        this.assignmentObservation.set(null); this.assignmentObservedActivity.set([]);
        this.assignmentMessage.set('Access changed while the assignment was unresolved. Check the current task again before continuing.');
        return;
      }
      const matches = workspace.tasks.filter(row => row.id === task.taskId);
      const observed = matches[0];
      if (workspace.engagementId !== task.engagementId || history.engagementId !== task.engagementId || history.taskId !== task.taskId ||
          workspace.truncated || matches.length !== 1 || !observed || observed.revision < task.revision) throw new Error('The exact current task and activity could not be verified. Keep the original request unresolved.');
      if (observed.revision === task.revision && this.assignmentTicket) {
        const readiness = this.actionRegistry.retryReadiness(this.assignmentTicket);
        if (!readiness.ok || readiness.value === 'in_flight') {
          this.assignmentMessage.set('The original request is still awaiting a response or cannot be verified. Check the task again after it finishes.');
          return;
        }
      }
      this.assignmentObservationOwner = scope; this.assignmentObservationRevision = accessRevision;
      if (this.assignmentTicket) this.verifiedActions.update(values => ({ ...values, [this.assignmentTicket!.item.requestId]: { owner: this.sessionOwnerKey(), revision: accessRevision } }));
      this.assignmentObservation.set(observed);
      this.assignmentObservedActivity.set(history.events.filter(event => event.taskRevision > task.revision &&
        ['TASK_ASSIGNED', 'TASK_REASSIGNED'].includes(event.kind)));
      this.assignmentMessage.set(observed.revision > task.revision
        ? 'The task revision advanced. Review and acknowledge the observed owner before opening another assignment.'
        : 'The task still has its original revision. An explicit retry uses the exact original owner and revision, so the server can refuse a concurrent change.');
    } catch {
      if (this.assignmentCurrent(scope, request) && accessRevision === this.availability.revision() && this.taskAccessCurrent()) {
        const id = this.assignmentTicket?.item.requestId;
        if (id) this.verifiedActions.update(values => { const next = { ...values }; delete next[id]; return next; });
        this.assignmentError.set('The current assignment could not be verified.');
        this.retainedError.set('The retained assignment could not be checked in the current scope.');
      }
    } finally {
      if (this.assignmentCurrent(scope, request)) {
        this.assignmentLoading.set(false);
        this.refreshReminderTask();
      }
    }
  }

  assignmentRetryOwner(): string { return this.assignmentIntent?.owner.displayName ?? this.assignmentIntent?.owner.userId ?? 'Unavailable'; }

  canReviewAssignmentRetry(): boolean {
    const intent = this.assignmentIntent, observed = this.assignmentObservation();
    const readiness = this.assignmentTicket ? this.actionRegistry.retryReadiness(this.assignmentTicket) : null;
    return this.assignmentAccessCurrent() && !!intent && !!this.assignmentTicket && intent.scope === this.assignmentScope() && this.assignmentUncertain() && !!observed &&
      !!readiness && readiness.ok && readiness.value === 'settled' &&
      this.assignmentObservationOwner === this.assignmentScope() && this.assignmentObservationRevision === this.availability.revision() &&
      observed.id === intent.task.taskId && observed.revision === intent.task.revision && observed.assigneeUserId === intent.task.assigneeUserId &&
      !['COMPLETED', 'CANCELLED'].includes(observed.status) && !this.assignmentBusy() && !this.assignmentLoading();
  }

  canRetryAssignment(): boolean { return this.canReviewAssignmentRetry() && this.assignmentReviewed(); }

  async retryAssignment(): Promise<void> {
    if (this.canRetryAssignment()) await this.dispatchAssignment(true);
  }

  acknowledgeAssignmentObservation(): void {
    const intent = this.assignmentIntent, observed = this.assignmentObservation();
    const registryOwner = this.registryOwner(), ticket = this.assignmentTicket;
    if (!this.assignmentAccessCurrent() || !registryOwner || !ticket || !intent || intent.scope !== this.assignmentScope() || !this.assignmentUncertain() || this.assignmentBusy() ||
        !observed || observed.revision <= intent.task.revision || this.assignmentObservationOwner !== this.assignmentScope() ||
        this.assignmentObservationRevision !== this.availability.revision()) return;
    const acknowledged = this.actionRegistry.acknowledge(registryOwner, ticket.item.requestId, { owner: registryOwner,
      engagementId: intent.task.engagementId, taskId: intent.task.taskId, revision: observed.revision,
      availabilityRevision: this.assignmentObservationRevision }, this.availability.revision());
    if (!acknowledged.ok) { this.loadRegistry(); this.assignmentFailed.set(true); this.assignmentMessage.set('The current assignment observation could not be acknowledged.'); return; }
    if (!this.taskAccessCurrent() || intent.scope !== this.assignmentScope() || this.assignmentObservationRevision !== this.availability.revision()) { this.loadRegistry(); return; }
    this.loadRegistry();
    const owner = observed.assigneeDisplayName ?? observed.assigneeUserId ?? 'Unassigned';
    this.resetAssignment(); this.refreshTaskWorkspaces(); this.refreshActivity();
    this.assignmentMessage.set(`Current owner ${owner} at revision ${observed.revision} acknowledged. The original assignment outcome is not established.`);
  }

  private registryLoadedOwner = '';

  private registryOwner(): WorkQueueRegistryOwner | null {
    const actor = this.session.current();
    return actor?.staff ? { firmId: actor.firmId, userId: actor.userId, generation: actor.generation,
      invalidation: this.session.invalidation(), staff: true } : null;
  }

  private clearSavedReceiptDiscovery(): void {
    ++this.receiptSequence;
    this.receiptCandidates = []; this.receiptOffset = 0;
    this.verifiedReceipts.set([]); this.receiptRecovery.set(null);
    this.receiptChecking.set(false); this.receiptMessage.set('');
  }

  findSavedReceipts(): void {
    if (!this.taskAccessCurrent() || this.receiptChecking()) return;
    this.clearSavedReceiptDiscovery();
    const owner = this.registryOwner();
    if (!owner) { this.receiptMessage.set('Saved receipts could not be checked.'); return; }
    const snapshot = this.receiptDiscovery.snapshot(owner);
    if (!snapshot.complete) {
      this.receiptMessage.set('Saved receipt discovery is incomplete. Existing recovery keys have been preserved.');
      return;
    }
    this.receiptCandidates = snapshot.candidates;
    if (!snapshot.candidates.length) { this.receiptMessage.set('No saved task receipts were found.'); return; }
    void this.checkMoreSavedReceipts();
  }

  hasMoreSavedReceipts(): boolean { return this.receiptOffset < this.receiptCandidates.length; }

  private receiptCurrent(entry: VerifiedReceipt): boolean {
    const owner = this.registryOwner();
    return !this.workQueueDestroyed && !!owner && this.taskAccessCurrent() && entry.owner === this.sessionOwnerKey() &&
      entry.revision === this.availability.revision() && this.receiptDiscovery.verify(owner, entry.candidate).valid;
  }

  visibleSavedReceipts(): VerifiedReceipt[] { return this.verifiedReceipts().filter(entry => this.receiptCurrent(entry)); }
  readonly receiptRecoveryAccessCurrent = () => {
    const entry = this.receiptRecovery();
    if (!entry || !this.receiptCurrent(entry)) return false;
    const panel = entry.candidate.kind === 'evidence' ? this.receiptEvidencePanel() :
      entry.candidate.kind === 'ordering' ? this.receiptOrderingPanel() : this.receiptApplicabilityPanel();
    return panel?.pending() === entry.candidate.requestKey;
  };

  private async readSavedReceiptTask(entry: VerifiedReceipt): Promise<boolean> {
    if (!this.receiptCurrent(entry)) return false;
    const candidate = entry.candidate;
    const workspace = await this.api.get(`/api/ui/engagements/${candidate.engagementId}/workflow/tasks?taskId=${encodeURIComponent(candidate.taskId)}`, decodeWorkflowTaskWorkspace);
    if (!this.receiptCurrent(entry) || workspace.truncated || workspace.tasks.length !== 1) return false;
    // Existing panels construct case-sensitive storage keys from the session and input IDs,
    // and compare the returned workspace IDs exactly. Refuse incompatible spellings.
    const actor = this.session.current();
    const prefix = { evidence: 'auditsphere.workflow-evidence', ordering: 'auditsphere.workflow-ordering',
      applicability: 'auditsphere.workflow-applicability' }[candidate.kind];
    return !!actor && workspace.engagementId === candidate.engagementId && workspace.tasks[0].id === candidate.taskId &&
      candidate.storageKey === `${prefix}:${actor.firmId}:${actor.userId}:${candidate.engagementId}:${candidate.taskId}`;
  }

  async checkMoreSavedReceipts(): Promise<void> {
    if (!this.taskAccessCurrent() || this.receiptChecking() || !this.hasMoreSavedReceipts()) return;
    const sequence = ++this.receiptSequence, owner = this.sessionOwnerKey(), revision = this.availability.revision();
    const current = () => !this.workQueueDestroyed && sequence === this.receiptSequence && owner === this.sessionOwnerKey() &&
      revision === this.availability.revision() && this.taskAccessCurrent();
    this.receiptChecking.set(true);
    let incomplete = false;
    const end = Math.min(this.receiptOffset + 10, this.receiptCandidates.length);
    while (current() && this.receiptOffset < end) {
      const candidate = this.receiptCandidates[this.receiptOffset++];
      const entry = { candidate, owner, revision };
      try {
        const valid = await this.readSavedReceiptTask(entry);
        if (!current()) return;
        if (valid) this.verifiedReceipts.update(items => [...items, entry]);
        else incomplete = true;
      } catch { if (!current()) return; incomplete = true; }
    }
    if (current()) {
      this.receiptChecking.set(false);
      if (incomplete) this.receiptMessage.set('Some saved receipts could not be checked. Their keys have been preserved.');
    }
  }

  async openSavedReceipt(entry: VerifiedReceipt): Promise<void> {
    if (this.receiptChecking() || this.receiptRecovery() || !this.visibleSavedReceipts().includes(entry)) return;
    const candidate = entry.candidate;
    const existing = candidate.kind === 'evidence' ? this.evidenceTask() : candidate.kind === 'ordering' ? this.orderingTask() : this.applicabilityTask();
    if (existing?.taskId.toLowerCase() === candidate.taskId.toLowerCase()) {
      this.receiptMessage.set('This receipt panel is already open. Close it before opening saved recovery.'); return;
    }
    const sequence = ++this.receiptSequence;
    this.receiptChecking.set(true); this.receiptMessage.set('');
    try {
      const valid = await this.readSavedReceiptTask(entry);
      if (sequence !== this.receiptSequence || !this.receiptCurrent(entry)) return;
      if (valid) this.receiptRecovery.set(entry);
      else this.receiptMessage.set('The saved receipt could not be checked. Its key has been preserved.');
    } catch {
      if (sequence === this.receiptSequence && this.receiptCurrent(entry))
        this.receiptMessage.set('The saved receipt could not be checked. Its key has been preserved.');
    } finally {
      if (sequence === this.receiptSequence) this.receiptChecking.set(false);
    }
  }

  closeSavedReceipt(): void { this.receiptRecovery.set(null); }
  savedReceiptConfirmed(): void {
    this.clearSavedReceiptDiscovery();
    this.receiptMessage.set('The saved receipt was confirmed. Find saved receipts again to check other outcomes.');
    this.refreshTaskWorkspaces(); this.refreshActivity();
  }

  private panelAccess(task: { taskId: string; engagementId: string } | null,
    own: 'evidence' | 'ordering' | 'applicability' | 'cancellation'): boolean {
    const owner = this.registryOwner();
    const active = own === 'cancellation' ? this.cancellationPanel()?.activeRegistryRequest() : null;
    const exempt = active && owner && active.firmId === owner.firmId.toLowerCase() && active.userId === owner.userId.toLowerCase() &&
      active.dispatchGeneration === owner.generation && active.dispatchInvalidation === owner.invalidation &&
      active.engagementId === task?.engagementId.toLowerCase() && active.taskId === task?.taskId.toLowerCase() ? active.requestId : null;
    if (active && (!exempt || !this.actionVerified(exempt))) return false;
    if (!task || !owner || this.panelConflict(task, own, exempt)) return false;
    if (own !== 'cancellation') {
      const panel = own === 'evidence' ? this.evidencePanel() : own === 'ordering' ? this.orderingPanel() : this.applicabilityPanel();
      const prefix = own === 'evidence' ? 'auditsphere.workflow-evidence' : own === 'ordering' ? 'auditsphere.workflow-ordering' : 'auditsphere.workflow-applicability';
      try {
        const saved = sessionStorage.getItem(`${prefix}:${owner.firmId}:${owner.userId}:${task.engagementId}:${task.taskId}`);
        if (saved !== null && panel?.pending() !== saved) return false;
      } catch { return false; }
    }
    const live = this.actionRegistry.read(owner);
    return live.ok && !live.value.some(item => item.taskId.toLowerCase() === task.taskId.toLowerCase() && item.requestId !== exempt);
  }
  readonly evidenceAccessCurrent = () => this.panelAccess(this.evidenceTask(), 'evidence');
  readonly orderingAccessCurrent = () => this.panelAccess(this.orderingTask(), 'ordering');
  readonly applicabilityAccessCurrent = () => this.panelAccess(this.applicabilityTask(), 'applicability');
  readonly cancellationAccessCurrent = () => this.panelAccess(this.cancellationTask(), 'cancellation');
  readonly assignmentAccessCurrent = () => this.taskAccessCurrent() && (!this.assignmentTicket || this.actionVerified(this.assignmentTicket.item.requestId));

  actionVerified(id: string): boolean {
    const item = this.verifiedActions()[id];
    return !!item && item.owner === this.sessionOwnerKey() && item.revision === this.availability.revision();
  }

  cancellationRegistryChanged(): void {
    const panel = this.cancellationPanel(), active = panel?.activeRegistryRequest();
    this.loadRegistry();
    if (active && !this.seenCancellationRequests.has(active.requestId)) {
      this.seenCancellationRequests.add(active.requestId);
      if (panel?.state() === 'review' && this.taskAccessCurrent())
        this.verifiedActions.update(items => ({ ...items, [active.requestId]: { owner: this.sessionOwnerKey(), revision: this.availability.revision() } }));
    }
  }

  cancellationScopeFailed(): void {
    const id = this.cancellationPanel()?.activeRegistryRequest()?.requestId;
    if (id) this.verifiedActions.update(values => { const next = { ...values }; delete next[id]; return next; });
    this.retainedError.set('The retained task action could not be checked in the current scope.');
  }

  loadRegistry(): void {
    const owner = this.registryOwner();
    this.registryLoadedOwner = this.sessionOwnerKey();
    if (!owner) { this.registryRecords.set([]); this.reconciliation.set(null); this.registryReady.set(false); this.registryError.set(''); return; }
    const result = this.actionRegistry.read(owner);
    const pending = this.actionRegistry.pendingReconciliation(owner);
    this.registryReady.set(result.ok && pending.ok);
    if (result.ok && pending.ok) {
      if (JSON.stringify(this.registryRecords()) !== JSON.stringify(result.value)) this.registryRecords.set(result.value);
      const previous = this.reconciliation();
      if (JSON.stringify(previous) !== JSON.stringify(pending.value)) {
        this.reconciliation.set(pending.value);
        const panel = this.cancellationPanel();
        const finishingLiveCancellation = panel?.busy() && panel.activeRegistryRequest()?.requestId === pending.value?.item.requestId &&
          !['cleanup', 'finalizing'].includes(panel.state());
        if (pending.value && !finishingLiveCancellation && (!previous || previous.item.requestId !== pending.value.item.requestId || previous.kind !== pending.value.kind)) {
          const id = pending.value.item.requestId;
          this.verifiedActions.update(values => { const next = { ...values }; delete next[id]; return next; });
          this.actionObservations.update(values => { const next = { ...values }; delete next[id]; return next; });
        }
      }
      this.registryError.set('');
    }
    else { this.registryRecords.set([]); this.reconciliation.set(pending.ok ? pending.value : null); this.registryError.set('Task action recovery storage is unavailable or invalid. Restore it before making task changes.'); }
  }

  private clearActionObservations(): void {
    this.observationRequests.clear(); this.verifiedActions.set({}); this.actionObservations.set({});
    this.observationErrors.set({}); this.loadingObservations.set([]); this.verifyingRetained.set(false); this.retainedError.set('');
  }

  actionInFlight(id: string): boolean {
    const panel = this.cancellationPanel();
    return this.runningActions().includes(id) || this.assignmentBusy() && this.assignmentTicket?.item.requestId === id ||
      !!panel && panel.busy() && panel.activeRegistryRequest()?.requestId === id;
  }
  private rememberUnsent(ticket: WorkQueueDispatchTicket): void {
    this.unsentTickets.set(ticket.item.requestId, ticket);
    this.unsentIds.update(ids => ids.includes(ticket.item.requestId) ? ids : [...ids, ticket.item.requestId]);
  }
  private rememberDefinitiveCleanup(ticket: WorkQueueDispatchTicket, success: boolean, message: string): void {
    this.definitiveCleanupTickets.set(ticket.item.requestId, { ticket, success, message });
    this.definitiveCleanupIds.update(ids => ids.includes(ticket.item.requestId) ? ids : [...ids, ticket.item.requestId]);
  }
  finishDefinitiveCleanup(id: string): void {
    const cleanup = this.definitiveCleanupTickets.get(id);
    if (!cleanup || !this.taskAccessCurrent() || !this.actionVerified(id)) return;
    const result = this.actionRegistry.removeDefinitiveTicket(cleanup.ticket);
    if (!result.ok) { this.actionFailed.set(true); this.actionMessage.set('The recovery reference could not be verified or removed.'); return; }
    if (!this.taskAccessCurrent() || !this.actionVerified(id) || !this.actionRegistry.finishDefinitiveTicket(cleanup.ticket).ok) {
      this.actionFailed.set(true); this.actionMessage.set('The recovery reference cleanup could not be completed in the current scope.'); return;
    }
    this.definitiveCleanupTickets.delete(id); this.definitiveCleanupIds.update(ids => ids.filter(value => value !== id));
    if (this.assignmentTicket === cleanup.ticket) this.resetAssignment();
    this.loadRegistry(); this.actionFailed.set(!cleanup.success); this.actionMessage.set(cleanup.message);
    this.refreshTaskWorkspaces(); this.refreshActivity();
  }
  clearUnsentReservation(id: string): void {
    const ticket = this.unsentTickets.get(id);
    if (!ticket || !this.taskAccessCurrent() || !this.actionObservations()[id] || !this.actionVerified(id)) return;
    const result = this.actionRegistry.abortBeforeDispatch(ticket);
    if (!result.ok) { this.actionFailed.set(true); this.actionMessage.set('The unsent recovery reference could not be verified or removed.'); return; }
    this.unsentTickets.delete(id); this.unsentIds.update(ids => ids.filter(value => value !== id));
    if (this.assignmentTicket === ticket) this.resetAssignment();
    this.loadRegistry();
    this.verifiedActions.update(values => { const next = { ...values }; delete next[id]; return next; });
    this.actionFailed.set(false); this.actionMessage.set('The unsent recovery reference was removed.');
  }
  actionLabel(action: string): string {
    return ({ complete: 'completion', changeDueDate: 'due-date change', cancel: 'cancellation', reopen: 'reopening',
      suspend: 'suspension', resume: 'resumption', assign: 'assignment', reassign: 'reassignment' } as Record<string, string>)[action] ?? 'task action';
  }
  observationLoading(id: string): boolean { return this.loadingObservations().includes(id); }
  visibleActions(): WorkQueuePendingAction[] {
    if (!this.taskAccessCurrent() || this.registryLoadedOwner !== this.sessionOwnerKey()) return [];
    const owner = this.sessionOwnerKey(), revision = this.availability.revision(), verified = this.verifiedActions();
    return this.registryRecords().filter(item => verified[item.requestId]?.owner === owner && verified[item.requestId]?.revision === revision);
  }
  private retainedActionsForVerification(): WorkQueuePendingAction[] {
    const items = [...this.registryRecords()], panel = this.cancellationPanel(), active = panel?.activeRegistryRequest(), owner = this.registryOwner();
    if (!owner) return items;
    const include = (item: WorkQueuePendingAction) => {
      if (item.firmId === owner.firmId.toLowerCase() && item.userId === owner.userId.toLowerCase() &&
          item.dispatchGeneration === owner.generation && item.dispatchInvalidation === owner.invalidation &&
          !items.some(existing => existing.requestId === item.requestId)) items.push(item);
    };
    if (active && panel && ['cleanup', 'finalizing'].includes(panel.state())) include(active);
    for (const cleanup of this.definitiveCleanupTickets.values()) include(cleanup.ticket.item);
    const pending = this.reconciliation();
    if (pending?.sameSession) {
      const index = items.findIndex(item => item.requestId === pending.item.requestId);
      if (index < 0) items.push({ ...pending.item }); else items[index] = { ...pending.item };
    }
    return items;
  }

  priorSessionFenceAvailable(): boolean {
    return !!this.registryOwner() && this.registryLoadedOwner === this.sessionOwnerKey() && !!this.reconciliation() && !this.reconciliation()!.sameSession;
  }

  /** Only the registry's prior-session byte check may release this lock; it does not remove an action. */
  releasePriorSessionFence(): void {
    const owner = this.registryOwner(), ownerKey = this.sessionOwnerKey(), pending = this.reconciliation();
    if (!owner || !pending || pending.sameSession || !this.priorSessionFenceAvailable()) return;
    const live = this.actionRegistry.pendingReconciliation(owner);
    if (!live.ok || !live.value || live.value.sameSession || JSON.stringify(live.value) !== JSON.stringify(pending)) {
      this.loadRegistry(); this.priorFenceMessage.set('Earlier session storage could not be checked.'); return;
    }
    const result = this.actionRegistry.releasePriorSessionFence(owner);
    if (ownerKey !== this.sessionOwnerKey()) return;
    this.loadRegistry();
    this.priorFenceMessage.set(result.ok ? 'The storage check sent no task action and does not establish the outcome of any earlier action.' :
      'Earlier session storage could not be checked. Try again when storage is available.');
  }

  /** Recovered capabilities only reconcile storage; they never resend or establish a command outcome. */
  finishReconciliation(): void {
    const owner = this.registryOwner(), pending = this.reconciliation();
    const observed = pending ? this.actionObservations()[pending.item.requestId] : null;
    if (!owner || !pending || !pending.sameSession || !observed || !this.taskAccessCurrent() || !this.actionVerified(pending.item.requestId) ||
        observed.owner !== this.sessionOwnerKey() || observed.revision !== this.availability.revision() ||
        observed.row.id.toLowerCase() !== pending.item.taskId.toLowerCase()) return;
    const live = this.actionRegistry.pendingReconciliation(owner);
    if (!live.ok || !live.value || live.value.ticket !== pending.ticket || JSON.stringify(live.value) !== JSON.stringify(pending)) {
      this.loadRegistry(); this.actionFailed.set(true); this.actionMessage.set('Recovery storage changed. Check the current task again.'); return;
    }
    const revision = this.availability.revision();
    let result;
    if (!pending.ticket) return;
    else if (pending.kind === 'cleanup') result = this.actionRegistry.abortBeforeDispatch(pending.ticket);
    else if (pending.kind === 'acknowledgement') result = this.actionRegistry.reconcileAcknowledgement(owner, pending.ticket,
      { owner, engagementId: pending.item.engagementId, taskId: pending.item.taskId, revision: observed.row.revision,
        availabilityRevision: observed.revision }, revision);
    else {
      result = this.actionRegistry.removeDefinitiveTicket(pending.ticket);
      if (result.ok && this.taskAccessCurrent() && this.sessionOwnerKey() === observed.owner && revision === this.availability.revision())
        result = this.actionRegistry.finishDefinitiveTicket(pending.ticket);
    }
    if (!result.ok) { this.loadRegistry(); this.actionFailed.set(true); this.actionMessage.set('Recovery storage could not be reconciled.'); return; }
    if (!this.taskAccessCurrent() || this.sessionOwnerKey() !== observed.owner || revision !== this.availability.revision()) { this.loadRegistry(); return; }
    const id = pending.item.requestId;
    this.unsentTickets.delete(id); this.unsentIds.update(ids => ids.filter(value => value !== id));
    this.definitiveCleanupTickets.delete(id); this.definitiveCleanupIds.update(ids => ids.filter(value => value !== id));
    if (this.assignmentTicket?.item.requestId === id) this.resetAssignment();
    if (this.cancellationPanel()?.activeRegistryRequest()?.requestId === id) this.closeCancellation();
    this.verifiedActions.update(values => { const next = { ...values }; delete next[id]; return next; });
    this.actionObservations.update(values => { const next = { ...values }; delete next[id]; return next; });
    this.loadRegistry(); this.actionFailed.set(false);
    this.actionMessage.set('Recovery storage reconciled. The original action outcome is not established.');
    this.refreshTaskWorkspaces();
  }
  hasUnverifiedActions(): boolean {
    return this.registryReady() && this.retainedActionsForVerification().some(item => !this.actionVerified(item.requestId));
  }

  private savedReceiptConflict(task: { taskId: string; engagementId: string }, own: 'evidence' | 'ordering' | 'applicability' | 'assignment' | 'cancellation' | null): boolean {
    const actor = this.session.current();
    if (!actor) return true;
    const prefixes = ['auditsphere.workflow-evidence', 'auditsphere.workflow-ordering', 'auditsphere.workflow-applicability'];
    const ownPrefix = own === 'evidence' ? prefixes[0] : own === 'ordering' ? prefixes[1] : own === 'applicability' ? prefixes[2] : null;
    const ownKey = `${ownPrefix}:${actor.firmId}:${actor.userId}:${task.engagementId}:${task.taskId}`;
    try {
      const length = sessionStorage.length;
      if (length > WORK_QUEUE_RECEIPT_SCAN_MAX_ENTRIES) return true;
      const keys: string[] = [];
      let chars = 0, conflict = false;
      for (let i = 0; i < length; i++) {
        const key = sessionStorage.key(i);
        if (key === null) return true;
        chars += key.length;
        if (chars > WORK_QUEUE_RECEIPT_SCAN_MAX_CHARS) return true;
        keys.push(key);
        const parts = key.split(':');
        if (prefixes.includes(parts[0]) && parts[1]?.toLowerCase() === actor.firmId.toLowerCase() &&
            parts[2]?.toLowerCase() === actor.userId.toLowerCase() && parts[3]?.toLowerCase() === task.engagementId.toLowerCase() &&
            parts[4]?.toLowerCase() === task.taskId.toLowerCase()) {
          if (key !== ownKey) conflict = true;
          else {
            const value = sessionStorage.getItem(key);
            if (value === null || guid(value, 'saved receipt') === '00000000-0000-0000-0000-000000000000') conflict = true;
          }
        }
      }
      if (sessionStorage.length !== length || keys.some((key, i) => sessionStorage.key(i) !== key)) return true;
      return conflict;
    } catch { return true; }
  }

  /** Other panels own their own receipt fences. Never overwrite or ignore those references. */
  private panelConflict(task: { taskId: string; engagementId: string }, own: 'evidence' | 'ordering' | 'applicability' | 'assignment' | 'cancellation' | null, exempt: string | null = null): boolean {
    if (!this.taskAccessCurrent() || !this.registryReady() || this.registryLoadedOwner !== this.sessionOwnerKey() ||
        this.registryRecords().some(item => item.taskId.toLowerCase() === task.taskId.toLowerCase() && item.requestId !== exempt)) return true;
    const same = (other: { taskId: string } | null) => other?.taskId.toLowerCase() === task.taskId.toLowerCase();
    if (same(this.receiptRecovery()?.candidate ?? null)) return true;
    if (own !== 'evidence' && same(this.evidenceTask()) || own !== 'ordering' && same(this.orderingTask()) ||
        own !== 'applicability' && same(this.applicabilityTask()) || own !== 'assignment' && same(this.assignmentTask()) ||
        own !== 'cancellation' && same(this.cancellationTask())) return true;
    return this.savedReceiptConflict(task, own);
  }

  taskMutationBlocked(taskId: string): boolean {
    const task = (this.queueLocationValid() ? this.queue.data()?.tasks.find(x => x.taskId === taskId) : null) ?? this.reminderQueueTask();
    return !task || task.taskId !== taskId || this.panelConflict(task, null);
  }

  changeDueDate(engagementId: string, taskId: string, revision: number, dueDate: string): void {
    void this.dispatchTaskAction(engagementId, taskId, revision, 'changeDueDate', dueDate || null);
  }
  complete(engagementId: string, taskId: string, revision: number): void {
    void this.dispatchTaskAction(engagementId, taskId, revision, 'complete', null);
  }

  private async dispatchTaskAction(engagementId: string, taskId: string, revision: number,
    action: 'complete' | 'changeDueDate', requestedDueDate: string | null): Promise<void> {
    const owner = this.registryOwner(), ownerKey = this.sessionOwnerKey(), accessRevision = this.availability.revision();
    if (!owner || !this.taskAccessCurrent()) return;
    this.loadRegistry();
    if (this.panelConflict({ engagementId, taskId }, null)) {
      this.actionFailed.set(true); this.actionMessage.set('Resolve or close the existing task action before making another change.'); return;
    }
    let requestId: string;
    try { requestId = crypto.randomUUID(); } catch { this.actionFailed.set(true); this.actionMessage.set('A recovery reference could not be created. No action was sent.'); return; }
    const reservation = this.actionRegistry.reserveForDispatchRecoverable(owner, { engagementId, taskId, expectedRevision: revision, action, requestedDueDate, requestId });
    if (!reservation.ok) {
      if ('cleanup' in reservation) this.rememberUnsent(reservation.cleanup);
      this.actionFailed.set(true); this.actionMessage.set('The task action could not be safely reserved. No action was sent.'); this.loadRegistry(); return;
    }
    const ticket = reservation.value, pending = ticket.item;
    this.loadRegistry();
    if (!this.registryReady() || ownerKey !== this.sessionOwnerKey() || !this.taskAccessCurrent() || accessRevision !== this.availability.revision()) {
      const aborted = this.actionRegistry.abortBeforeDispatch(ticket);
      if (ownerKey === this.sessionOwnerKey()) { if (!aborted.ok) this.rememberUnsent(ticket); this.loadRegistry(); this.actionFailed.set(true); this.actionMessage.set(aborted.ok ? 'Access changed. No action was sent.' : 'No action was sent; the recovery reference could not be cleared.'); }
      return;
    }
    this.verifiedActions.update(v => ({ ...v, [pending.requestId]: { owner: ownerKey, revision: accessRevision } }));
    this.runningActions.update(ids => [...ids, pending.requestId]);
    this.actionMessage.set(''); this.actionFailed.set(false);
    const path = action === 'complete' ? 'complete' : 'due-date';
    const body = action === 'complete' ? { expectedRevision: revision } : { dueDate: requestedDueDate, expectedRevision: revision };
    let result: CommandOutcome;
    try {
      const invoked = this.actionRegistry.invokeReserved(ticket, () => this.api.command(`/api/ui/engagements/${engagementId}/workflow/tasks/${taskId}/${path}`, body));
      if (!invoked.ok) {
        const aborted = this.actionRegistry.abortBeforeDispatch(ticket);
        if (ownerKey === this.sessionOwnerKey()) {
          this.runningActions.update(ids => ids.filter(id => id !== pending.requestId)); if (!aborted.ok) this.rememberUnsent(ticket); this.loadRegistry();
          this.actionFailed.set(true); this.actionMessage.set(aborted.ok ? 'The action was not sent.' : 'No action was sent; the recovery reference could not be cleared.');
        }
        return;
      }
      result = await invoked.value;
    }
    catch {
      if (!this.workQueueDestroyed && ownerKey === this.sessionOwnerKey()) {
        this.runningActions.update(ids => ids.filter(id => id !== pending.requestId));
        this.actionFailed.set(true); this.actionMessage.set('The task action outcome is unconfirmed. Check the exact task; no action was resent.');
      }
      return;
    }
    if (this.workQueueDestroyed || ownerKey !== this.sessionOwnerKey()) return;
    this.runningActions.update(ids => ids.filter(id => id !== pending.requestId));
    if (!this.taskAccessCurrent() || accessRevision !== this.availability.revision()) {
      this.actionFailed.set(true); this.actionMessage.set('Access changed. Check the retained task action after access returns.'); return;
    }
    if (result.ok && result.value !== true) {
      this.actionFailed.set(true); this.actionMessage.set('The task action response could not be confirmed. Check the exact task; no action was resent.');
      this.refreshTaskWorkspaces(); return;
    }
    if (result.ok || !result.unknown) {
      const removed = this.actionRegistry.removeDefinitiveTicket(ticket);
      const finished = removed.ok && ownerKey === this.sessionOwnerKey() && this.taskAccessCurrent() && accessRevision === this.availability.revision()
        ? this.actionRegistry.finishDefinitiveTicket(ticket) : null;
      if (!removed.ok || !finished?.ok) { this.rememberDefinitiveCleanup(ticket, result.ok, result.ok ? (action === 'complete' ? 'Task completed.' : 'Task due date updated.') : 'The task action was refused. Review the current task.'); this.loadRegistry(); this.actionFailed.set(true); this.actionMessage.set('The response was received, but the recovery record could not be cleared.'); return; }
      this.loadRegistry();
      this.actionFailed.set(!result.ok); this.actionMessage.set(result.ok ? (action === 'complete' ? 'Task completed.' : 'Task due date updated.') : result.message);
      this.refreshActivity(); this.refreshTaskWorkspaces(); return;
    }
    this.actionFailed.set(true); this.actionMessage.set('The task action outcome is unconfirmed. Check the exact task; no action was resent.');
    this.refreshTaskWorkspaces();
  }

  async verifyRetainedActions(): Promise<void> {
    if (!this.taskAccessCurrent() || this.verifyingRetained()) return;
    const owner = this.sessionOwnerKey(), revision = this.availability.revision();
    this.verifyingRetained.set(true); this.retainedError.set('');
    for (const item of this.retainedActionsForVerification()) {
      if (this.workQueueDestroyed || owner !== this.sessionOwnerKey() || revision !== this.availability.revision() || !this.taskAccessCurrent()) return;
      if (!this.actionVerified(item.requestId) && !this.actionInFlight(item.requestId)) await this.observeAction(item.requestId);
    }
    if (!this.workQueueDestroyed && owner === this.sessionOwnerKey() && revision === this.availability.revision()) {
      this.verifyingRetained.set(false);
      if (this.hasUnverifiedActions()) this.retainedError.set('Some retained task actions could not be verified in your current scope.');
    }
  }

  async observeAction(requestId: string): Promise<void> {
    const task = this.retainedActionsForVerification().find(item => item.requestId === requestId);
    if (!task || !this.registryReady() || this.actionInFlight(requestId) || this.observationLoading(requestId) || !this.taskAccessCurrent()) return;
    const owner = this.sessionOwnerKey(), revision = this.availability.revision(), request = ++this.observationSequence;
    this.observationRequests.set(requestId, request);
    this.loadingObservations.update(ids => [...ids, requestId]);
    this.actionObservations.update(values => { const next = { ...values }; delete next[requestId]; return next; });
    this.observationErrors.update(values => { const next = { ...values }; delete next[requestId]; return next; });
    const current = () => !this.workQueueDestroyed && this.observationRequests.get(requestId) === request &&
      owner === this.sessionOwnerKey() && revision === this.availability.revision() && this.taskAccessCurrent() &&
      this.retainedActionsForVerification().some(item => item.requestId === requestId);
    try {
      const workspace = await this.api.get(`/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${encodeURIComponent(task.taskId)}`, decodeWorkflowTaskWorkspace);
      if (!current()) return;
      const matches = workspace.tasks.filter(row => row.id.toLowerCase() === task.taskId);
      if (workspace.engagementId.toLowerCase() !== task.engagementId || workspace.truncated || workspace.tasks.length !== 1 || matches.length !== 1 || matches[0].revision < task.expectedRevision ||
          task.action === 'changeDueDate' && matches[0].dueDate === undefined) throw new Error('The exact current task could not be verified.');
      this.verifiedActions.update(values => ({ ...values, [requestId]: { owner, revision } }));
      this.actionObservations.update(values => ({ ...values, [requestId]: { row: matches[0], owner, revision } }));
    } catch (error) {
      if (current()) {
        this.verifiedActions.update(values => { const next = { ...values }; delete next[requestId]; return next; });
        this.actionObservations.update(values => { const next = { ...values }; delete next[requestId]; return next; });
        this.observationErrors.update(values => ({ ...values, [requestId]: error instanceof Error ? error.message : 'The exact current task could not be verified.' }));
        this.retainedError.set('A retained task action could not be checked in the current scope.');
      }
    } finally {
      if (current()) { this.loadingObservations.update(ids => ids.filter(id => id !== requestId)); this.observationRequests.delete(requestId); }
    }
  }

  acknowledgeAction(requestId: string): void {
    const owner = this.registryOwner(), task = this.registryRecords().find(item => item.requestId === requestId), observed = this.actionObservations()[requestId];
    if (!owner || !task || !observed || this.actionInFlight(requestId) || !this.taskAccessCurrent() ||
        observed.owner !== this.sessionOwnerKey() || observed.revision !== this.availability.revision()) return;
    const result = this.actionRegistry.acknowledge(owner, requestId, { owner, engagementId: task.engagementId,
      taskId: task.taskId, revision: observed.row.revision, availabilityRevision: observed.revision }, this.availability.revision());
    if (!result.ok) { this.loadRegistry(); this.actionFailed.set(true); this.actionMessage.set('The current task observation could not be acknowledged.'); return; }
    if (!this.taskAccessCurrent() || observed.owner !== this.sessionOwnerKey() || observed.revision !== this.availability.revision()) { this.loadRegistry(); return; }
    if (this.assignmentTicket?.item.requestId === requestId && !this.assignmentBusy()) this.resetAssignment();
    if (this.cancellationPanel()?.activeRegistryRequest()?.requestId === requestId && !this.cancellationPanel()?.busy()) this.closeCancellation();
    this.loadRegistry();
    this.actionObservations.update(values => { const next = { ...values }; delete next[requestId]; return next; });
    this.verifiedActions.update(values => { const next = { ...values }; delete next[requestId]; return next; });
    this.actionFailed.set(false); this.actionMessage.set('Current task observation acknowledged. The original action outcome is not established.');
    this.refreshTaskWorkspaces();
  }
}
