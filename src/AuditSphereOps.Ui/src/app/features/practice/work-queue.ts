import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { Api, CommandState } from '../../core/api';
import { arr, bool, date, guid, instant, int, nullable, obj, oneOf, str, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const nonBlank = (max: number) => (value: unknown, path: string): string => {
  const decoded = str(max)(value, path);
  if (!decoded.trim()) throw new Error(`${path}: expected non-empty text`);
  return decoded;
};
const queueTask = obj({
  taskId: guid, clientId: guid, clientName: text, engagementId: guid, engagementServiceRoute: str(100),
  engagementPeriod: str(80), title: str(300), phase: str(40), ordinal: int, taskStatus: str(40),
  assigneeUserId: nullable(guid), assigneeDisplayName: nullable(str(200)), dueDate: nullable(date),
  mappingVersionId: nullable(guid), fsliCode: nullable(str(100)), unmetTaskDependencies: int,
  canComplete: bool, canChangeDueDate: bool, revision: int,
  pbcRequestId: nullable(guid), pbcState: nullable(str(32)),
  reviewState: nullable(str(30)), reviewComment: nullable(str(20000)),
});
const decodeQueue = obj({
  view: str(30), tasks: arr(queueTask, 100), hasMore: bool,
  nextCursor: nullable(obj({ dueDate: nullable(date), taskId: guid })),
});
const activityEvent = obj({
  id: guid, kind: oneOf('TASK_CREATED', 'TASK_ASSIGNED', 'TASK_REASSIGNED', 'TASK_COMPLETED', 'TASK_DUE_DATE_CHANGED'),
  sourceLabel: oneOf('Operational Task'), actorDisplayName: nonBlank(200),
  assigneeDisplayName: nullable(str(200)), taskRevision: int,
  previousDueDate: nullable(date), newDueDate: nullable(date), occurredAt: instant,
});
export const decodeActivity = obj({
  engagementId: guid, taskId: guid, from: date, to: date, events: arr(activityEvent, 101),
  hasMore: bool, nextCursor: nullable(obj({ occurredAt: instant, activityId: guid })),
});

type QueueView = 'MY_TASKS' | 'TEAM' | 'WAITING_FOR_CLIENT' | 'CHANGES_REQUIRED' | 'OVERDUE' | 'COMPLETED';
const queueViews: { key: QueueView; label: string }[] = [
  { key: 'MY_TASKS', label: 'My tasks' },
  { key: 'TEAM', label: 'Team' },
  { key: 'WAITING_FOR_CLIENT', label: 'Waiting for client' },
  { key: 'CHANGES_REQUIRED', label: 'Changes required' },
  { key: 'OVERDUE', label: 'Overdue' },
  { key: 'COMPLETED', label: 'Completed' },
];

@Component({
  selector: 'audit-work-queue',
  imports: [MatButtonModule, RouterLink, ...SHARED],
  template: `
    <audit-page-header title="My work" eyebrow="Engagement tasks"
      description="Operational tasks across your current client and engagement grants. Task completion never replaces audit review or engagement approval." />
    <p><a matButton routerLink="/app/practice/workflow-templates">Manage workflow templates</a></p>
    <audit-state [loading]="queue.loading()" [error]="queue.error()" label="work queue" />
    <nav class="queue-views" aria-label="Work queue views">
      @for (item of views; track item.key) {
        <button matButton [attr.aria-pressed]="selectedView() === item.key" (click)="selectView(item.key)">{{ item.label }}</button>
      }
    </nav>
    @if (queue.data(); as result) {
      <p role="status">{{ result.tasks.length }} {{ result.view === 'MY_TASKS' ? 'assigned to you' : 'in this authorized view' }}. Client and engagement rows are limited to your current grants.</p>
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
                @if (task.reviewState === 'CHANGES_REQUIRED') {
                  <small><audit-status [value]="task.reviewState" /> · current audit result needs resubmission</small>
                  @if (task.reviewComment) { <small>Reviewer: {{ task.reviewComment }}</small> }
                }
              </td>
              <td>{{ task.assigneeDisplayName ?? 'Unassigned' }}</td>
              <td><audit-status [value]="task.taskStatus" /></td>
              <td>{{ task.unmetTaskDependencies ? task.unmetTaskDependencies + ' task prerequisite(s) incomplete' : 'No task prerequisites pending' }}</td>
              <td><button matButton (click)="showActivity(task.engagementId, task.taskId, task.title)">{{ activityTask()?.taskId === task.taskId ? 'Hide activity' : 'View activity' }}</button>
                @if (task.canComplete) { <button matButton="filled" (click)="complete(task.engagementId, task.taskId, task.revision)" [disabled]="command.busy()">Complete task</button> }</td>
            </tr>
          } @empty {
            <tr><td colspan="7">No tasks match this view in your current authorized scope.</td></tr>
          }
        </tbody>
      </table></div>
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
      <p class="queue-note">Waiting for client is derived from the linked PBC request state. Changes required is derived from the current procedure result and clears when a newer result is submitted. Task completion never changes audit review or approval state.</p>
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
  readonly views = queueViews;
  readonly selectedView = signal<QueueView>('MY_TASKS');
  readonly activityTask = signal<{ engagementId: string; taskId: string; title: string } | null>(null);
  private readonly cursor = signal<{ dueDate: string | null; taskId: string } | null>(null);
  private readonly activityCursor = signal<{ occurredAt: string; activityId: string } | null>(null);
  readonly queue = this.api.resource(() => {
    const params = new URLSearchParams({ view: this.selectedView(), pageSize: '50' });
    const after = this.cursor();
    if (after) {
      if (after.dueDate) params.set('afterDueDate', after.dueDate);
      params.set('afterTaskId', after.taskId);
    }
    return `/api/ui/work/tasks?${params.toString()}`;
  }, decodeQueue, 'The work queue is unavailable in your current scope or is not enabled on this server.');
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

  labelFor(value: string): string {
    return this.views.find(x => x.key === value)?.label ?? 'Work';
  }

  selectView(value: QueueView): void {
    this.cursor.set(null);
    this.selectedView.set(value);
  }

  nextPage(cursor: { dueDate: string | null; taskId: string }): void {
    this.cursor.set(cursor);
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
      TASK_DUE_DATE_CHANGED: 'Due date changed' } as Record<string, string>)[kind] ?? 'Workflow event';
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
