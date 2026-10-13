import { NgTemplateOutlet } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { Api, CommandState } from '../../core/api';
import { arr, bool, date, guid, instant, int, nullable, obj, oneOf, str, text } from '../../core/decode';
import { SHARED } from '../../core/ui';
import { SessionService } from '../../core/session';
import { QUEUE_DISPLAYS, QueueDisplay, boardColumns, queueCalendar } from './work-queue-views';
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
  canComplete: bool, canChangeDueDate: bool, revision: int,
  pbcRequestId: nullable(guid), pbcState: nullable(str(32)),
  reviewState: nullable(str(30)), reviewComment: nullable(str(20000)),
});
const decodeQueue = obj({
  view: str(30), tasks: arr(queueTask, 100), hasMore: bool,
  nextCursor: nullable(obj({ dueDate: nullable(date), taskId: guid })),
});
const reviewItem = obj({ resultId: guid, procedureId: guid, engagementId: guid, clientName: text,
  serviceRoute: str(100), engagementPeriod: str(80), sourceProcedureId: str(100), procedureTitle: text,
  revision: int, preparedByDisplayName: str(200), submittedAt: instant });
const decodeReviewInbox = obj({ reviews: arr(reviewItem, 100), hasMore: bool,
  nextCursor: nullable(obj({ submittedAt: instant, resultId: guid })) });
const activityEvent = obj({
  id: guid, kind: oneOf('TASK_CREATED', 'TASK_ASSIGNED', 'TASK_REASSIGNED', 'TASK_COMPLETED', 'TASK_DUE_DATE_CHANGED', 'TASK_EVIDENCE_RULE_CHANGED'),
  sourceLabel: oneOf('Operational Task'), actorDisplayName: nonBlank(200),
  assigneeDisplayName: nullable(str(200)), taskRevision: int,
  previousDueDate: nullable(date), newDueDate: nullable(date), occurredAt: instant,
});
export const decodeActivity = obj({
  engagementId: guid, taskId: guid, from: date, to: date, events: arr(activityEvent, 101),
  hasMore: bool, nextCursor: nullable(obj({ occurredAt: instant, activityId: guid })),
});

type QueueView = 'MY_TASKS' | 'TEAM' | 'WAITING_FOR_CLIENT' | 'UNDER_REVIEW' | 'CHANGES_REQUIRED' | 'OVERDUE' | 'COMPLETED';
const queueViews: { key: QueueView; label: string }[] = [
  { key: 'MY_TASKS', label: 'My tasks' },
  { key: 'TEAM', label: 'Team' },
  { key: 'WAITING_FOR_CLIENT', label: 'Waiting for client' },
  { key: 'UNDER_REVIEW', label: 'Under review' },
  { key: 'CHANGES_REQUIRED', label: 'Changes required' },
  { key: 'OVERDUE', label: 'Overdue' },
  { key: 'COMPLETED', label: 'Completed' },
];

@Component({
  selector: 'audit-work-queue',
  imports: [MatButtonModule, RouterLink, NgTemplateOutlet, WorkflowTaskEvidencePanel, ...SHARED],
  template: `
    <audit-page-header title="My work" eyebrow="Engagement tasks"
      description="Operational tasks across your current client and engagement grants. Task completion never replaces audit review or engagement approval." />
    <p><a matButton routerLink="/app/practice/workflow-templates">Manage workflow templates</a></p>
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
        @if (task.unmetTaskDependencies) { <small>{{ task.unmetTaskDependencies }} task prerequisite(s) incomplete</small> }
        @if (task.unmetEvidencePrerequisites) { <small>{{ task.unmetEvidencePrerequisites }} evidence prerequisite(s) pending</small> }
        <button matButton (click)="evidenceTask.set(task)">Review prerequisites</button>
        @if (task.canComplete) {
          <button matButton="filled" (click)="complete(task.engagementId, task.taskId, task.revision)" [disabled]="command.busy()">Complete task</button>
        }
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
              <td><audit-status [value]="task.taskStatus" /></td>
              <td>{{ task.unmetTaskDependencies ? task.unmetTaskDependencies + ' task prerequisite(s) incomplete' : 'No task prerequisites pending' }}
                @if (task.unmetEvidencePrerequisites) { <small>{{ task.unmetEvidencePrerequisites }} evidence prerequisite(s) pending</small> }
                <button matButton (click)="evidenceTask.set(task)">Review prerequisites</button></td>
              <td><button matButton (click)="showActivity(task.engagementId, task.taskId, task.title)">{{ activityTask()?.taskId === task.taskId ? 'Hide activity' : 'View activity' }}</button>
                @if (task.canComplete) { <button matButton="filled" (click)="complete(task.engagementId, task.taskId, task.revision)" [disabled]="command.busy()">Complete task</button> }</td>
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
  readonly board = boardColumns;
  readonly calendar = queueCalendar;
  readonly selectedView = signal<QueueView>('MY_TASKS');
  readonly activityTask = signal<{ engagementId: string; taskId: string; title: string } | null>(null);
  readonly evidenceTask = signal<{ engagementId: string; taskId: string; title: string } | null>(null);
  private readonly cursor = signal<{ dueDate: string | null; taskId: string } | null>(null);
  private readonly reviewCursor = signal<{ submittedAt: string; resultId: string } | null>(null);
  private readonly activityCursor = signal<{ occurredAt: string; activityId: string } | null>(null);
  readonly queue = this.api.resource(() => {
    const params = new URLSearchParams({ view: this.selectedView(), pageSize: '50' });
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
      this.evidenceTask.set(null); this.activityTask.set(null);
    });
    effect(() => {
      const selected = this.evidenceTask(); const page = this.queue.data();
      if (selected && !page?.tasks.some(x => x.taskId === selected.taskId && x.engagementId === selected.engagementId))
        this.evidenceTask.set(null);
    });
  }

  labelFor(value: string): string {
    return this.views.find(x => x.key === value)?.label ?? 'Work';
  }

  selectView(value: QueueView): void {
    this.evidenceTask.set(null);
    this.cursor.set(null);
    this.ownerFilter.set('ALL');
    this.selectedView.set(value);
  }

  selectOwnerFilter(value: string): void {
    this.evidenceTask.set(null);
    if (this.selectedView() === 'MY_TASKS' || !['ALL', 'ME', 'UNASSIGNED'].includes(value)) return;
    this.cursor.set(null);
    this.ownerFilter.set(value as 'ALL' | 'ME' | 'UNASSIGNED');
  }

  nextPage(cursor: { dueDate: string | null; taskId: string }): void {
    this.evidenceTask.set(null);
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
      TASK_DUE_DATE_CHANGED: 'Due date changed', TASK_EVIDENCE_RULE_CHANGED: 'Evidence prerequisite changed' } as Record<string, string>)[kind] ?? 'Workflow event';
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
