import { Component, DestroyRef, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { Api, CommandState } from '../../core/api';
import { Drafts } from '../../core/drafts';
import { arr, bool, date, guid, nat, obj, text } from '../../core/decode';
import { SHARED } from '../../core/ui';

const entry = obj({ id: guid, workDate: date, taskId: guid, taskTitle: text, userId: guid, durationMinutes: nat, activity: text,
  billableClassification: text, narrative: text, status: text });
export const decodeTime = obj({ firmWide: bool, isApprover: bool, periods: arr(obj({ id: guid, clientId: guid, label: text }), 5000),
  openTasks: arr(obj({ id: guid, title: text, status: text }), 5000), myEntries: arr(entry, 500), awaitingApproval: arr(entry, 500) });

@Component({
  selector: 'audit-practice-time',
  imports: [FormsModule, MatButtonModule, RouterLink, ...SHARED],
  template: `
    <audit-page-header title="Practice time & task records" eyebrow="Work & collaboration"
      description="Time entries are recorded against assigned firm tasks with exact integer minutes, immutable rate snapshots, and independent approval segregation." />
    <audit-state [loading]="view.loading()" [error]="view.error()" label="time records" />
    @if (view.data(); as v) {
      <p role="status">{{ v.openTasks.length }} open tasks in scope · {{ v.myEntries.length }} of my time entries shown{{ v.isApprover ? ' · ' + v.awaitingApproval.length + ' awaiting my review' : '' }}</p>
      <section class="panel" aria-labelledby="task-heading">
        <h2 id="task-heading">Create work task</h2>
        <p>Link an optional accounting period when the task belongs to a client reporting workflow. Due dates are optional and are never inferred.</p>
        <form class="inline-form" (submit)="$event.preventDefault(); createTask()">
          <label>Task title <input name="title" [(ngModel)]="task.title" (ngModelChange)="touchTask()" required maxlength="200" autocomplete="off" /></label>
          <label>Reporting period (optional) <select name="period" [(ngModel)]="task.period" (ngModelChange)="touchTask()">
            <option value="" [disabled]="!v.firmWide">No period link (firm-wide task)</option>
            @for (p of v.periods; track p.id) { <option [value]="p.id">{{ p.label }}</option> }</select></label>
          <label>Due date (optional) <input type="date" name="due" [(ngModel)]="task.due" (ngModelChange)="touchTask()" /></label>
          <button matButton="filled" type="submit" [disabled]="cmd.busy()">Create task</button>
        </form>
      </section>
      <section class="panel" aria-labelledby="log-heading">
        <h2 id="log-heading">Record time draft</h2>
        @if (!v.openTasks.length) { <p>No open tasks are currently available to book time against. Create or assign an open task first.</p> }
        @else {
          <form class="inline-form" (submit)="$event.preventDefault(); saveDraft()">
            <label>Work task <select name="task" [(ngModel)]="draft.task" (ngModelChange)="touchDraft()" required>@for (t of v.openTasks; track t.id) { <option [value]="t.id">{{ t.title }} ({{ t.status }})</option> }</select></label>
            <label>Date <input type="date" name="date" [(ngModel)]="draft.date" (ngModelChange)="touchDraft()" required /></label>
            <label>Duration (minutes) <input type="number" name="minutes" min="15" step="15" [(ngModel)]="draft.minutes" (ngModelChange)="touchDraft()" required /></label>
            <label>Role <input name="role" [(ngModel)]="draft.role" (ngModelChange)="touchDraft()" placeholder="Staff, Manager, Partner" required /></label>
            <label>Activity <input name="activity" [(ngModel)]="draft.activity" (ngModelChange)="touchDraft()" placeholder="Fieldwork, Review, Planning" required /></label>
            <label><span><input type="checkbox" name="billable" [(ngModel)]="draft.billable" (ngModelChange)="touchDraft()" /> Billable to client (optional)</span></label>
            <label>Narrative (optional) <textarea name="narrative" [(ngModel)]="draft.narrative" (ngModelChange)="touchDraft()" rows="2" maxlength="2000"></textarea></label>
            <button matButton="filled" type="submit" [disabled]="cmd.busy()">Save time draft</button>
          </form>
        }
      </section>
      <section class="panel" aria-labelledby="my-time-heading">
        <h2 id="my-time-heading">My recorded time</h2>
        <div class="table-scroll"><table>
          <caption>My recorded time entries</caption>
          <thead><tr><th scope="col">Date</th><th scope="col">Task</th><th scope="col">Duration</th><th scope="col">Classification</th><th scope="col">Status</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
          <tbody>@for (e of v.myEntries; track e.id) {
            <tr><td>{{ e.workDate }}</td><td>{{ e.taskTitle }}</td><td>{{ e.durationMinutes }}m ({{ (e.durationMinutes / 60).toFixed(1) }}h)</td><td>{{ e.billableClassification }}</td>
              <td><audit-status [value]="e.status" /></td>
              <td>@if (e.status === 'DRAFT') { <button matButton (click)="act(e.id, 'submit', 'Time entry was submitted for approval.')" [disabled]="cmd.busy()">Submit</button> }</td></tr>
          } @empty { <tr><td colspan="6">You have not recorded any time entries in the current firm scope.</td></tr> }</tbody>
        </table></div>
      </section>
      <section class="panel" aria-labelledby="rates-heading">
        <h2 id="rates-heading">Charge-out rates</h2>
        <p>A time entry captures the approved charge-out rate for its role and activity as a snapshot. Rates are governed on the <a routerLink="/app/practice/rate-cards">charge-out rates</a> page, where a rate takes effect only after a separate approver approves it.</p>
      </section>
      @if (v.isApprover) {
        <section class="panel" aria-labelledby="queue-heading">
          <h2 id="queue-heading">Time approval queue</h2>
          <p>Independent approval segregation: managers and partners cannot approve their own time entries.</p>
          <div class="table-scroll"><table>
            <caption>Time entries awaiting my approval</caption>
            <thead><tr><th scope="col">Date</th><th scope="col">Staff user</th><th scope="col">Task</th><th scope="col">Duration</th><th scope="col">Activity</th><th scope="col">Narrative</th><th scope="col"><span class="sr-only">Action</span></th></tr></thead>
            <tbody>@for (e of v.awaitingApproval; track e.id) {
              <tr><td>{{ e.workDate }}</td><td><code>{{ e.userId }}</code></td><td>{{ e.taskTitle }}</td><td>{{ e.durationMinutes }}m</td><td>{{ e.activity }}</td><td>{{ e.narrative }}</td>
                <td><button matButton="filled" (click)="act(e.id, 'approve', 'Time entry was approved.')" [disabled]="cmd.busy()">Approve</button></td></tr>
            } @empty { <tr><td colspan="7">No pending submitted time entries awaiting approval.</td></tr> }</tbody>
          </table></div>
        </section>
      }
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
})
export class PracticeTime {
  private readonly api = inject(Api);
  private readonly drafts = inject(Drafts);
  readonly view = this.api.resource(() => '/api/ui/practice/time', decodeTime, 'Sign in with an authorized internal staff identity to view and record time.');
  readonly cmd = new CommandState(this.api);
  task = { title: '', period: '', due: '' };
  draft = { task: '', date: new Date().toISOString().slice(0, 10), minutes: 60, role: 'Staff', activity: 'Fieldwork', billable: true, narrative: '' };
  private taskTimer?: ReturnType<typeof setTimeout>;
  private draftTimer?: ReturnType<typeof setTimeout>;
  private static validTask(value: unknown): PracticeTime['task'] | null {
    if (!value || typeof value !== 'object') return null;
    const v = value as Record<string, unknown>;
    return typeof v['title'] === 'string' && v['title'].length <= 200 && typeof v['period'] === 'string' &&
      typeof v['due'] === 'string' ? { title: v['title'], period: v['period'], due: v['due'] } : null;
  }
  private static validDraft(value: unknown): PracticeTime['draft'] | null {
    if (!value || typeof value !== 'object') return null;
    const v = value as Record<string, unknown>;
    return typeof v['task'] === 'string' && typeof v['date'] === 'string' && typeof v['minutes'] === 'number' &&
      typeof v['role'] === 'string' && v['role'].length <= 100 && typeof v['activity'] === 'string' && v['activity'].length <= 100 &&
      typeof v['billable'] === 'boolean' && typeof v['narrative'] === 'string' && v['narrative'].length <= 2000
      ? { task: v['task'], date: v['date'], minutes: v['minutes'], role: v['role'], activity: v['activity'], billable: v['billable'], narrative: v['narrative'] }
      : null;
  }
  constructor() {
    const savedTask = this.drafts.load('practice-task', PracticeTime.validTask);
    if (savedTask) this.task = savedTask;
    const savedDraft = this.drafts.load('practice-time-draft', PracticeTime.validDraft);
    if (savedDraft) this.draft = savedDraft;
    inject(DestroyRef).onDestroy(() => { clearTimeout(this.taskTimer); clearTimeout(this.draftTimer); });
  }
  touchTask(): void {
    clearTimeout(this.taskTimer);
    this.taskTimer = setTimeout(() => this.drafts.save('practice-task', this.task), 800);
  }
  touchDraft(): void {
    clearTimeout(this.draftTimer);
    this.draftTimer = setTimeout(() => this.drafts.save('practice-time-draft', this.draft), 800);
  }

  private reload = () => this.view.reload();
  createTask(): void {
    this.cmd.run('/api/ui/practice/time/tasks', { title: this.task.title, reportingPeriodId: this.task.period || null, dueDate: this.task.due || null },
      'Work task created.', () => { this.task = { title: '', period: '', due: '' }; this.drafts.clear('practice-task'); }).finally(this.reload);
  }
  saveDraft(): void {
    const taskId = this.draft.task || this.view.data()?.openTasks[0]?.id;
    const minutes = Math.trunc(Number(this.draft.minutes));
    if (!taskId || !Number.isSafeInteger(minutes) || minutes <= 0) { this.cmd.failed.set(true); this.cmd.message.set('Choose a task and a whole number of minutes.'); return; }
    this.cmd.run('/api/ui/practice/time/entries', { taskId, workDate: this.draft.date, durationMinutes: minutes, role: this.draft.role,
      activity: this.draft.activity, billable: this.draft.billable, narrative: this.draft.narrative },
      `Time draft recorded (${minutes}m).`, () => { this.draft.narrative = ''; this.drafts.clear('practice-time-draft'); }).finally(this.reload);
  }
  act(id: string, action: 'submit' | 'approve', ok: string): void {
    this.cmd.run(`/api/ui/practice/time/entries/${id}/${action}`, {}, ok).finally(this.reload);
  }
}
