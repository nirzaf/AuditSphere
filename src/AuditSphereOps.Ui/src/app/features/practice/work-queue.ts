import { NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { Api, CommandState } from '../../core/api';
import { arr, bool, date, guid, instant, int, nullable, obj, oneOf, str, text } from '../../core/decode';
import { SHARED } from '../../core/ui';
import { SessionService } from '../../core/session';
import { QUEUE_DISPLAYS, QueueDisplay, boardColumns, queueCalendar } from './work-queue-views';
import { WorkflowTaskDependenciesPanel } from './workflow-task-dependencies';
import { WorkflowTaskEvidencePanel } from './workflow-task-evidence';

const nonBlank = (max: number) => (value: unknown, path: string): string => {
  const decoded = str(max)(value, path);
  if (!decoded.trim()) throw new Error(`${path}: expected non-empty text`);
  return decoded;
};
const queueTask = obj({
  taskId: guid, clientId: guid, clientName: text, engagementId: guid, engagementServiceRoute: str(100),
  engagementPeriod: str(80), title: str(300), phase: str(40), ordinal: int, taskStatus: str(40),
  assigneeUserId: nullable(guid), assigneeDisplayName: nullable(str(200)), dueDate: nullable(date),
  mappingVersionId: nullable(guid), fsliCode: nullable(str(100)), unmetTaskDependencies: int, unmetEvidencePrerequisites: int,
  canComplete: bool, canChangeDueDate: bool, canAssign: bool, revision: int,
  pbcRequestId: nullable(guid), pbcState: nullable(str(32)),
  reviewState: nullable(str(30)), reviewComment: nullable(str(20000)),
});
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
const decodeAssigneeOptions = arr(obj({ userId: guid, displayName: nonBlank(200) }), 200);
const decodeAssignmentWorkspace = obj({
  engagementId: guid, truncated: bool,
  tasks: arr(obj({ id: guid, assigneeUserId: nullable(guid), assigneeDisplayName: nullable(str(200)), revision: int, status: str(40) }), 500),
});
type AssignmentIntent = { task: QueueTask; owner: { userId: string; displayName: string }; action: 'assign' | 'reassign'; scope: string };
type AssignmentObservation = ReturnType<typeof decodeAssignmentWorkspace>['tasks'][number];
const reviewItem = obj({ resultId: guid, procedureId: guid, engagementId: guid, clientName: text,
  serviceRoute: str(100), engagementPeriod: str(80), sourceProcedureId: str(100), procedureTitle: text,
  revision: int, preparedByDisplayName: str(200), submittedAt: instant });
const decodeReviewInbox = obj({ reviews: arr(reviewItem, 100), hasMore: bool,
  nextCursor: nullable(obj({ submittedAt: instant, resultId: guid })) });
const activityEvent = obj({
  id: guid, kind: oneOf('TASK_CREATED', 'TASK_ASSIGNED', 'TASK_REASSIGNED', 'TASK_COMPLETED', 'TASK_DUE_DATE_CHANGED', 'TASK_EVIDENCE_RULE_CHANGED', 'TASK_DEPENDENCIES_CHANGED'),
  sourceLabel: oneOf('Operational Task'), actorDisplayName: nonBlank(200),
  assigneeDisplayName: nullable(str(200)), taskRevision: int,
  previousDueDate: nullable(date), newDueDate: nullable(date), occurredAt: instant,
});
export const decodeActivity = obj({
  engagementId: guid, taskId: guid, from: date, to: date, events: arr(activityEvent, 101),
  hasMore: bool, nextCursor: nullable(obj({ occurredAt: instant, activityId: guid })),
});

type QueueView = 'MY_TASKS' | 'TEAM' | 'DUE_TODAY' | 'UPCOMING' | 'WAITING_FOR_CLIENT' | 'UNDER_REVIEW' | 'CHANGES_REQUIRED' | 'OVERDUE' | 'COMPLETED';
const queueViews: { key: QueueView; label: string }[] = [
  { key: 'MY_TASKS', label: 'My tasks' },
  { key: 'TEAM', label: 'Team' },
  { key: 'DUE_TODAY', label: 'Due today' },
  { key: 'UPCOMING', label: 'Next 7 days' },
  { key: 'WAITING_FOR_CLIENT', label: 'Waiting for client' },
  { key: 'UNDER_REVIEW', label: 'Under review' },
  { key: 'CHANGES_REQUIRED', label: 'Changes required' },
  { key: 'OVERDUE', label: 'Overdue' },
  { key: 'COMPLETED', label: 'Completed' },
];

@Component({
  selector: 'audit-work-queue',
  imports: [MatButtonModule, RouterLink, NgTemplateOutlet, WorkflowTaskEvidencePanel, WorkflowTaskDependenciesPanel, ...SHARED],
  template: `
    <audit-page-header title="My work" eyebrow="Engagement tasks"
      description="Operational tasks across your current client and engagement grants. Task completion never replaces audit review or engagement approval." />
    <p><a matButton routerLink="/app/practice/workflow-templates">Manage workflow templates</a></p>
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
    <section aria-labelledby="review-inbox-heading">
      <h2 id="review-inbox-heading">Submitted reviews without task links</h2>
      <p>Current submitted procedure results with no operational task for their procedure. Opening fieldwork rechecks review eligibility and current evidence.</p>
      <audit-state [loading]="reviews.loading()" [error]="reviews.error()" label="review inbox" />
      @if (reviews.data(); as inbox) {
        <p role="status">{{ inbox.reviews.length }} unlinked submitted review(s) in your reviewer and engagement scope.</p>
        <div class="table-scroll"><table>
          <caption>Procedure reviews awaiting attention without an operational task</caption>
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
              <tr><td colspan="5">No unlinked submitted procedures await review in your authorized scope.</td></tr>
            }
          </tbody>
        </table></div>
        @if (inbox.hasMore && inbox.nextCursor) {
          <button matButton (click)="nextReviewPage(inbox.nextCursor)">Next reviews</button>
        }
      }
    </section>
    <audit-state [loading]="queue.loading()" [error]="queue.error()" label="work queue" />
    @if (selectedView() !== 'MY_TASKS') {
      <label>Task assignment
        <select [value]="ownerFilter()" (change)="selectOwnerFilter($any($event.target).value)">
          <option value="ALL">All authorized assignments</option>
          <option value="ME">Assigned to me</option>
          <option value="UNASSIGNED">Unassigned</option>
        </select>
      </label>
    }
    <nav class="queue-views" aria-label="Work queue views">
      @for (item of views; track item.key) {
        <button matButton [attr.aria-pressed]="selectedView() === item.key" (click)="selectView(item.key)">{{ item.label }}</button>
      }
    </nav>
    <nav class="queue-views" aria-label="Work queue display">
      @for (item of displays; track item.key) {
        <button matButton [attr.aria-pressed]="display() === item.key" (click)="display.set(item.key)">{{ item.label }}</button>
      }
    </nav>
    <ng-template #card let-task>
      <article class="task-card">
        <strong>{{ task.title }}</strong>
        <small><a [routerLink]="['/app/engagements', task.engagementId]">{{ task.clientName }}</a> · {{ task.engagementServiceRoute }} · {{ task.engagementPeriod }}</small>
        <small>{{ task.phase.replaceAll('_', ' ') }} · {{ task.dueDate ? 'due ' + task.dueDate : 'no due date' }} · {{ task.assigneeDisplayName ?? 'Unassigned' }}</small>
        <small><audit-status [value]="task.taskStatus" />@if (task.reviewState) { · <audit-status [value]="task.reviewState" /> }@if (task.pbcState) { · PBC {{ task.pbcState }} }</small>
        @if (blockedPrerequisites(task)) { <small class="prerequisite-status">Prerequisites: <audit-status value="BLOCKED" /></small> }
        @if (task.unmetTaskDependencies) { <small>{{ task.unmetTaskDependencies }} task prerequisite(s) incomplete</small> }
        @if (task.unmetEvidencePrerequisites) { <small>{{ task.unmetEvidencePrerequisites }} evidence prerequisite(s) pending</small> }
        <button matButton (click)="evidenceTask.set(task)">Review prerequisites</button>
        <button matButton (click)="orderingTask.set(task)">Review task ordering</button>
        @if (task.canComplete) {
          <button matButton="filled" (click)="complete(task.engagementId, task.taskId, task.revision)" [disabled]="command.busy()">Complete task</button>
        }
        @if (task.canAssign) { <button matButton (click)="openAssignment(task)"> {{ task.assigneeUserId ? 'Reassign task' : 'Assign task' }} </button> }
      </article>
    </ng-template>
    @if (queue.data(); as result) {
      <p role="status">{{ result.tasks.length }} {{ result.view === 'MY_TASKS' ? 'assigned to you' : 'in this authorized view' }}. Client and engagement rows are limited to your current grants.</p>
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
      @if (display() === 'LIST') {
      <div class="table-scroll"><table>
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
                    <button matButton (click)="changeDueDate(task.engagementId, task.taskId, task.revision, dueDateInput.value)">Save date</button>
                  </div>
                }
              </td>
              <td><a [routerLink]="['/app/engagements', task.engagementId]">{{ task.clientName }}</a><small>{{ task.engagementServiceRoute }} · {{ task.engagementPeriod }}</small></td>
              <td>{{ task.title }}<small>{{ task.phase.replace('_', ' ') }} · #{{ task.ordinal }}@if (task.fsliCode) { · FSLI {{ task.fsliCode }} }@if (task.mappingVersionId) { · mapping {{ task.mappingVersionId.slice(0, 8) }} }</small>
                @if (task.pbcRequestId) { <small><a [routerLink]="['/app/engagements', task.engagementId, 'pbc']">PBC request · {{ task.pbcState }}</a></small> }
                @if (task.reviewState) {
                  <small><audit-status [value]="task.reviewState" /> · {{ task.reviewState === 'UNDER_REVIEW' ? 'resubmitted work awaits independent review' : 'current audit result needs resubmission' }}</small>
                  @if (task.reviewComment) { <small>Reviewer: {{ task.reviewComment }}</small> }
                }
              </td>
              <td>{{ task.assigneeDisplayName ?? 'Unassigned' }}</td>
              <td><audit-status [value]="task.taskStatus" />
                @if (blockedPrerequisites(task)) { <small class="prerequisite-status">Prerequisites: <audit-status value="BLOCKED" /></small> }
              </td>
              <td>{{ task.unmetTaskDependencies ? task.unmetTaskDependencies + ' task prerequisite(s) incomplete' : 'No task prerequisites pending' }}
                @if (task.unmetEvidencePrerequisites) { <small>{{ task.unmetEvidencePrerequisites }} evidence prerequisite(s) pending</small> }
                <button matButton (click)="evidenceTask.set(task)">Review prerequisites</button>
        <button matButton (click)="orderingTask.set(task)">Review task ordering</button></td>
              <td><button matButton (click)="showActivity(task.engagementId, task.taskId, task.title)">{{ activityTask()?.taskId === task.taskId ? 'Hide activity' : 'View activity' }}</button>
                @if (task.canComplete) { <button matButton="filled" (click)="complete(task.engagementId, task.taskId, task.revision)" [disabled]="command.busy()">Complete task</button> }
                @if (task.canAssign) { <button matButton (click)="openAssignment(task)">{{ task.assigneeUserId ? 'Reassign task' : 'Assign task' }}</button> }</td>
            </tr>
          } @empty {
            <tr><td colspan="7">No tasks match this view in your current authorized scope.</td></tr>
          }
        </tbody>
      </table></div>
      }
      @if (evidenceTask(); as selected) {
        <audit-workflow-task-evidence [engagementId]="selected.engagementId" [taskId]="selected.taskId" [title]="selected.title"
          (changed)="queue.reload(); activity.reload()" />
        <button matButton (click)="evidenceTask.set(null)">Close prerequisites</button>
      }
      @if (orderingTask(); as selected) {
        <audit-workflow-task-dependencies [engagementId]="selected.engagementId" [taskId]="selected.taskId" [title]="selected.title"
          (changed)="queue.reload(); activity.reload()" />
        <button matButton (click)="orderingTask.set(null)">Close task ordering</button>
      }
      @if (activityTask(); as selected) {
        <section class="activity-panel" aria-labelledby="task-activity-heading">
          <h2 id="task-activity-heading">Task activity</h2>
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
      @if (assignmentTask(); as selected) {
        <section class="activity-panel" aria-labelledby="task-assignment-heading">
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
              } @else if (canRetryAssignment()) {
                <button matButton (click)="retryAssignment()" [disabled]="assignmentBusy()">Retry the exact original assignment</button>
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
    <audit-command-message [message]="command.message()" [failed]="command.failed()" />
  `,
  styleUrl: './work-queue.scss',
})
export class WorkflowWorkQueue {
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly ownerFilter = signal<'ALL' | 'ME' | 'UNASSIGNED'>('ALL');
  readonly views = queueViews;
  readonly displays = QUEUE_DISPLAYS;
  /** How the loaded page is arranged. It never changes which tasks are requested or shown. */
  readonly display = signal<QueueDisplay>('LIST');
  readonly blockedPrerequisites = hasBlockingPrerequisites;
  readonly board = boardColumns;
  readonly calendar = queueCalendar;
  readonly selectedView = signal<QueueView>('MY_TASKS');
  readonly activityTask = signal<{ engagementId: string; taskId: string; title: string } | null>(null);
  readonly orderingTask = signal<{ engagementId: string; taskId: string; title: string } | null>(null);
  readonly evidenceTask = signal<{ engagementId: string; taskId: string; title: string } | null>(null);
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
  private assignmentRequest = 0;
  private assignmentDestroyed = false;
  private readonly cursor = signal<{ dueDate: string | null; taskId: string } | null>(null);
  private readonly reviewCursor = signal<{ submittedAt: string; resultId: string } | null>(null);
  private readonly activityCursor = signal<{ occurredAt: string; activityId: string } | null>(null);
  readonly queue = this.api.resource(() => {
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
  }, decodeQueue, 'The work queue is unavailable in your current scope or is not enabled on this server.');
  readonly reviews = this.api.resource(() => {
    const params = new URLSearchParams({ pageSize: '50' });
    const after = this.reviewCursor();
    if (after) {
      params.set('afterSubmittedAt', after.submittedAt);
      params.set('afterResultId', after.resultId);
    }
    return `/api/ui/work/reviews?${params.toString()}`;
  }, decodeReviewInbox, 'The procedure review inbox is unavailable in your current reviewer scope or is not enabled on this server.');
  readonly activity = this.api.resource(() => {
    const selected = this.activityTask();
    if (!selected) return null;
    const params = new URLSearchParams({ pageSize: '50' });
    const after = this.activityCursor();
    if (after) {
      params.set('afterOccurredAt', after.occurredAt);
      params.set('afterActivityId', after.activityId);
    }
    return `/api/ui/engagements/${selected.engagementId}/workflow/tasks/${selected.taskId}/activity?${params.toString()}`;
  }, decodeActivity, 'Task activity is unavailable in your current authorized scope.');
  readonly command = new CommandState(this.api);

  constructor() {
    effect(() => {
      this.session.invalidation(); this.session.current();
      untracked(() => {
        this.evidenceTask.set(null); this.orderingTask.set(null); this.activityTask.set(null); this.resetAssignment();
      });
    });
    inject(DestroyRef).onDestroy(() => { this.assignmentDestroyed = true; ++this.assignmentRequest; });
    effect(() => {
      const ordering = this.orderingTask(); const page = this.queue.data();
      if (ordering && !page?.tasks.some(x => x.taskId === ordering.taskId && x.engagementId === ordering.engagementId)) this.orderingTask.set(null);
      const selected = this.evidenceTask();
      if (selected && !page?.tasks.some(x => x.taskId === selected.taskId && x.engagementId === selected.engagementId))
        this.evidenceTask.set(null);
    });
  }

  labelFor(value: string): string {
    return this.views.find(x => x.key === value)?.label ?? 'Work';
  }

  /** Groups an exact count string for display without converting it to a number. */
  groupCount(value: string): string {
    return value.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  }

  selectView(value: QueueView): void {
    this.evidenceTask.set(null); this.orderingTask.set(null);
    this.cursor.set(null);
    this.ownerFilter.set('ALL');
    this.selectedView.set(value);
  }

  selectOwnerFilter(value: string): void {
    this.evidenceTask.set(null); this.orderingTask.set(null);
    if (this.selectedView() === 'MY_TASKS' || !['ALL', 'ME', 'UNASSIGNED'].includes(value)) return;
    this.cursor.set(null);
    this.ownerFilter.set(value as 'ALL' | 'ME' | 'UNASSIGNED');
  }

  nextPage(cursor: { dueDate: string | null; taskId: string }): void {
    this.evidenceTask.set(null); this.orderingTask.set(null);
    this.cursor.set(cursor);
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
      TASK_DUE_DATE_CHANGED: 'Due date changed', TASK_EVIDENCE_RULE_CHANGED: 'Evidence prerequisite changed', TASK_DEPENDENCIES_CHANGED: 'Task ordering changed' } as Record<string, string>)[kind] ?? 'Workflow event';
  }

  async openAssignment(task: QueueTask): Promise<void> {
    if (!task.canAssign || this.assignmentBusy() || this.assignmentUncertain()) return;
    const scope = this.assignmentScope(), request = ++this.assignmentRequest;
    this.assignmentTask.set(task); this.assignmentOptions.set([]); this.assignmentUserId.set('');
    this.assignmentReviewed.set(false); this.assignmentError.set(''); this.assignmentMessage.set('');
    this.assignmentLoading.set(true);
    try {
      const options = await this.api.get(`/api/ui/engagements/${task.engagementId}/workflow/tasks/assignees`, decodeAssigneeOptions);
      if (this.assignmentCurrent(scope, request)) this.assignmentOptions.set(options);
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
    if (!task || !owner || !task.canAssign || !this.assignmentReviewed() || this.assignmentBusy() || this.assignmentUncertain() || owner.userId === task.assigneeUserId) return;
    this.assignmentIntent = { task: { ...task }, owner: { ...owner }, action: task.assigneeUserId ? 'reassign' : 'assign', scope: this.assignmentScope() };
    await this.dispatchAssignment(false);
  }

  private async dispatchAssignment(retry: boolean): Promise<void> {
    const intent = this.assignmentIntent;
    if (!intent || intent.scope !== this.assignmentScope()) return;
    const { task, owner, action, scope } = intent, request = ++this.assignmentRequest;
    this.assignmentBusy.set(true); this.assignmentFailed.set(false); this.assignmentMessage.set('');
    this.assignmentObservation.set(null); this.assignmentObservedActivity.set([]);
    const outcome = await this.api.command(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/${action}`,
      { assigneeUserId: owner.userId, expectedRevision: task.revision });
    if (!this.assignmentCurrent(scope, request)) return;
    this.assignmentBusy.set(false);
    if (outcome.ok) {
      this.resetAssignment();
      this.assignmentMessage.set(`Task assigned to ${owner.displayName}.`);
      this.queue.reload(); this.activity.reload();
      return;
    }
    this.assignmentFailed.set(true); this.assignmentMessage.set(outcome.message);
    if (outcome.unknown || retry) {
      this.assignmentUncertain.set(true); this.assignmentReviewed.set(false);
      this.assignmentMessage.set('The original assignment remains unconfirmed. Check the current task and its activity before retrying or choosing another owner.');
    } else if (outcome.status === 409 || /stale|revision/i.test(outcome.code)) {
      this.resetAssignment();
      this.assignmentFailed.set(true);
      this.queue.reload(); this.activity.reload();
      this.assignmentMessage.set('The task changed before assignment. The queue has been refreshed; reopen it and review the current owner and revision.');
    } else if (outcome.status === 403) {
      this.resetAssignment(); this.assignmentFailed.set(true); this.assignmentMessage.set(outcome.message); this.queue.reload();
    } else {
      this.assignmentIntent = null; this.assignmentReviewed.set(false);
    }
  }

  async recoverAssignment(): Promise<void> {
    const intent = this.assignmentIntent;
    if (!intent || !this.assignmentUncertain() || this.assignmentBusy() || this.assignmentLoading() || intent.scope !== this.assignmentScope()) return;
    const request = ++this.assignmentRequest, { task, scope } = intent;
    this.assignmentLoading.set(true); this.assignmentError.set('');
    this.assignmentObservation.set(null); this.assignmentObservedActivity.set([]);
    try {
      const [workspace, history] = await Promise.all([
        this.api.get(`/api/ui/engagements/${task.engagementId}/workflow/tasks`, decodeAssignmentWorkspace),
        this.api.get(`/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/activity?pageSize=50`, decodeActivity),
      ]);
      if (!this.assignmentCurrent(scope, request)) return;
      const observed = workspace.tasks.find(row => row.id === task.taskId);
      if (workspace.engagementId !== task.engagementId || history.engagementId !== task.engagementId || history.taskId !== task.taskId ||
          !observed || observed.revision < task.revision) throw new Error('The exact current task and activity could not be verified. Keep the original request unresolved.');
      this.assignmentObservation.set(observed);
      this.assignmentObservedActivity.set(history.events.filter(event => event.taskRevision > task.revision &&
        ['TASK_ASSIGNED', 'TASK_REASSIGNED'].includes(event.kind)));
      this.assignmentMessage.set(observed.revision > task.revision
        ? 'The task revision advanced. Review and acknowledge the observed owner before opening another assignment.'
        : 'The task still has its original revision. An explicit retry uses the exact original owner and revision, so the server can refuse a concurrent change.');
    } catch (error) {
      if (this.assignmentCurrent(scope, request)) this.assignmentError.set(error instanceof Error ? error.message : 'Current assignment could not be verified.');
    } finally { if (this.assignmentCurrent(scope, request)) this.assignmentLoading.set(false); }
  }

  canRetryAssignment(): boolean {
    const intent = this.assignmentIntent, observed = this.assignmentObservation();
    return !!intent && intent.scope === this.assignmentScope() && this.assignmentUncertain() && !!observed &&
      observed.id === intent.task.taskId && observed.revision === intent.task.revision && observed.assigneeUserId === intent.task.assigneeUserId &&
      !['COMPLETED', 'CANCELLED'].includes(observed.status) && !this.assignmentBusy() && !this.assignmentLoading();
  }

  async retryAssignment(): Promise<void> {
    if (this.canRetryAssignment()) await this.dispatchAssignment(true);
  }

  acknowledgeAssignmentObservation(): void {
    const intent = this.assignmentIntent, observed = this.assignmentObservation();
    if (!intent || intent.scope !== this.assignmentScope() || !this.assignmentUncertain() || this.assignmentBusy() ||
        !observed || observed.revision <= intent.task.revision) return;
    const owner = observed.assigneeDisplayName ?? observed.assigneeUserId ?? 'Unassigned';
    this.resetAssignment(); this.queue.reload(); this.activity.reload();
    this.assignmentMessage.set(`Observed assignment to ${owner} at revision ${observed.revision} acknowledged. Review the refreshed queue before another change.`);
  }

  changeDueDate(engagementId: string, taskId: string, revision: number, dueDate: string): void {
    this.command.run(`/api/ui/engagements/${engagementId}/workflow/tasks/${taskId}/due-date`,
      { dueDate: dueDate || null, expectedRevision: revision }, 'Task due date updated.', () => {
        this.queue.reload();
        this.activity.reload();
      }).finally(this.queue.reload);
  }

  complete(engagementId: string, taskId: string, revision: number): void {
    this.command.run(`/api/ui/engagements/${engagementId}/workflow/tasks/${taskId}/complete`,
      { expectedRevision: revision }, 'Task completed.', () => {
        this.queue.reload();
        this.activity.reload();
      }).finally(this.queue.reload);
  }
}
