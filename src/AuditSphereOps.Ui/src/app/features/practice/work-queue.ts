import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { Api, CommandState } from '../../core/api';
import { arr, bool, date, guid, int, nullable, obj, str, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const queueTask = obj({
  taskId: guid, clientId: guid, clientName: text, engagementId: guid, engagementServiceRoute: str(100),
  engagementPeriod: str(80), title: str(300), phase: str(40), ordinal: int, taskStatus: str(40),
  assigneeUserId: nullable(guid), assigneeDisplayName: nullable(str(200)), dueDate: nullable(date),
  mappingVersionId: nullable(guid), fsliCode: nullable(str(100)), unmetTaskDependencies: int,
  canComplete: bool, revision: int,
});
const decodeQueue = obj({
  view: str(30), tasks: arr(queueTask, 100), hasMore: bool,
  nextCursor: nullable(obj({ dueDate: nullable(date), taskId: guid })),
});

type QueueView = 'MY_TASKS' | 'TEAM' | 'OVERDUE' | 'COMPLETED';
const queueViews: { key: QueueView; label: string }[] = [
  { key: 'MY_TASKS', label: 'My tasks' },
  { key: 'TEAM', label: 'Team' },
  { key: 'OVERDUE', label: 'Overdue' },
  { key: 'COMPLETED', label: 'Completed' },
];

@Component({
  selector: 'audit-work-queue',
  imports: [MatButtonModule, RouterLink, ...SHARED],
  template: `
    <audit-page-header title="My work" eyebrow="Engagement tasks"
      description="Operational tasks across your current client and engagement grants. Task completion never replaces audit review or engagement approval." />
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
        <thead><tr><th scope="col">Due</th><th scope="col">Client / engagement</th><th scope="col">Task</th><th scope="col">Owner</th><th scope="col">Operational status</th><th scope="col">Prerequisites</th><th scope="col"><span class="sr-only">Action</span></th></tr></thead>
        <tbody>
          @for (task of result.tasks; track task.taskId) {
            <tr>
              <td>{{ task.dueDate ?? 'No due date' }}</td>
              <td><a [routerLink]="['/app/engagements', task.engagementId]">{{ task.clientName }}</a><small>{{ task.engagementServiceRoute }} · {{ task.engagementPeriod }}</small></td>
              <td>{{ task.title }}<small>{{ task.phase.replace('_', ' ') }} · #{{ task.ordinal }}@if (task.fsliCode) { · FSLI {{ task.fsliCode }} }@if (task.mappingVersionId) { · mapping {{ task.mappingVersionId.slice(0, 8) }} }</small></td>
              <td>{{ task.assigneeDisplayName ?? 'Unassigned' }}</td>
              <td><audit-status [value]="task.taskStatus" /></td>
              <td>{{ task.unmetTaskDependencies ? task.unmetTaskDependencies + ' task prerequisite(s) incomplete' : 'No task prerequisites pending' }}</td>
              <td>@if (task.canComplete) { <button matButton="filled" (click)="complete(task.engagementId, task.taskId, task.revision)" [disabled]="command.busy()">Complete task</button> }</td>
            </tr>
          } @empty {
            <tr><td colspan="7">No tasks match this view in your current authorized scope.</td></tr>
          }
        </tbody>
      </table></div>
      <p class="queue-note">Waiting for client, audit review, and changes-required states are derived from their authoritative workspaces and are not inferred from this task status.</p>
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
  private readonly cursor = signal<{ dueDate: string | null; taskId: string } | null>(null);
  readonly queue = this.api.resource(() => {
    const params = new URLSearchParams({ view: this.selectedView(), pageSize: '50' });
    const after = this.cursor();
    if (after) {
      if (after.dueDate) params.set('afterDueDate', after.dueDate);
      params.set('afterTaskId', after.taskId);
    }
    return `/api/ui/work/tasks?${params.toString()}`;
  }, decodeQueue, 'The work queue is unavailable in your current scope or is not enabled on this server.');
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

  complete(engagementId: string, taskId: string, revision: number): void {
    this.command.run(`/api/ui/engagements/${engagementId}/workflow/tasks/${taskId}/complete`,
      { expectedRevision: revision }, 'Task completed.', () => this.queue.reload()).finally(this.queue.reload);
  }
}
