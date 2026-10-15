import { Component, DestroyRef, HostListener, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api, CommandState } from '../../core/api';
import { Drafts } from '../../core/drafts';
import { arr, bool, date, DecodeError, Decoder, guid, nat, nullable, obj, str, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { NavigationProtected } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import { applyTracker, draftSegments, emptyTracker, TrackerDraft, TrackerState } from './time-tracker';

const entryShape = obj({ id: guid, workDate: date, taskId: guid, taskTitle: text, userId: guid, durationMinutes: nat, startMinute: nat, activity: text,
  billableClassification: text, narrative: text, status: text, mappingVersionId: nullable(guid), fsliCode: nullable(str(100)) });
const entry: Decoder<ReturnType<typeof entryShape>> = (value, path) => {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new DecodeError(`${path || 'response'}: expected object`);
  const source = value as Record<string, unknown>;
  return entryShape(Object.prototype.hasOwnProperty.call(source, 'startMinute') ? source : { ...source, startMinute: 540 }, path);
};
const taskOption = obj({ id: guid, title: text, status: text, mappingVersionId: nullable(guid), fsliCode: nullable(str(100)) });
export const decodeTime = obj({ firmWide: bool, isApprover: bool, periods: arr(obj({ id: guid, clientId: guid, label: text }), 5000),
  engagements: arr(obj({ id: guid, clientId: guid, label: text, mappingVersionId: nullable(guid), fsliCodes: arr(str(100), 2000) }), 5000),
  openTasks: arr(taskOption, 5000), myEntries: arr(entry, 500), awaitingApproval: arr(entry, 500) });

@Component({
  selector: 'audit-practice-time',
  imports: [FormsModule, MatButtonModule, MatDialogModule, RouterLink, ...SHARED],
  template: `
    <audit-page-header title="Practice time & task records" eyebrow="Work & collaboration"
      description="Time entries are recorded against assigned firm tasks with exact integer minutes, immutable rate snapshots, and independent approval segregation." />
    <audit-state [loading]="view.loading()" [error]="view.error()" label="time records" />
    @if (view.data(); as v) {
      <p role="status">{{ v.openTasks.length }} open tasks in scope · {{ v.myEntries.length }} of my time entries shown{{ v.isApprover ? ' · ' + v.awaitingApproval.length + ' awaiting my review' : '' }}</p>
      <section class="panel" aria-labelledby="task-heading">
        <h2 id="task-heading">Create work task</h2>
        <p>Scope a task to an authorized client period or engagement. Engagement tasks can optionally pin a destination from the current approved FSLI mapping; the exact mapping and code are copied to time entries. Due dates are never inferred.</p>
        <form class="inline-form" (submit)="$event.preventDefault(); createTask()">
          <label>Task title <input name="title" [(ngModel)]="task.title" (ngModelChange)="touchTask()" required maxlength="200" autocomplete="off" /></label>
          <label>Reporting period (optional) <select name="period" [(ngModel)]="task.period" (ngModelChange)="touchTask()">
            <option value="" [disabled]="!v.firmWide">No period link (firm-wide task)</option>
            @for (p of v.periods; track p.id) { <option [value]="p.id">{{ p.label }}</option> }</select></label>
          <label>Engagement (optional) <select name="engagement" [(ngModel)]="task.engagement" (ngModelChange)="engagementChanged()">
            <option value="">No engagement link</option>
            @for (e of v.engagements; track e.id) { <option [value]="e.id">{{ e.label }}</option> }</select></label>
          @if (selectedEngagement(); as engagement) {
            <label>FSLI mapping (optional) <select name="fsli" [(ngModel)]="task.fsli" (ngModelChange)="touchTask()">
              <option value="">No FSLI attribution</option>
              @for (code of engagement.fsliCodes; track code) { <option [value]="code">{{ code }}</option> }
            </select></label>
            @if (!engagement.mappingVersionId) { <p role="status">No approved mapping is available for this engagement. FSLI attribution remains unavailable.</p> }
          }
          <label>Due date (optional) <input type="date" name="due" [(ngModel)]="task.due" (ngModelChange)="touchTask()" /></label>
          <button matButton="filled" type="submit" [disabled]="cmd.busy()">Create task</button>
        </form>
      </section>
      <section class="panel" aria-labelledby="log-heading">
        <h2 id="log-heading">Record time draft</h2>
        @if (!v.openTasks.length) { <p>No open tasks are currently available to book time against. Create or assign an open task first.</p> }
        @else {
          <form class="inline-form" (submit)="$event.preventDefault(); saveDraft()">
            <label>Work task <select name="task" [(ngModel)]="draft.task" (ngModelChange)="touchDraft()" required>@for (t of v.openTasks; track t.id) { <option [value]="t.id">{{ t.title }} ({{ t.status }}){{ t.fsliCode ? ' · FSLI ' + t.fsliCode + ' · map ' + (t.mappingVersionId ?? '').slice(0, 8) : '' }}</option> }</select></label>
            <label>Work date (UTC) <input type="date" name="date" [(ngModel)]="draft.date" (ngModelChange)="touchDraft()" required /></label>
            <label>Start time (UTC) <input type="time" name="startTime" [(ngModel)]="draft.startTime" (ngModelChange)="touchDraft()" required /></label>
            <label>Duration (minutes) <input type="number" name="minutes" min="1" max="1440" step="1" [(ngModel)]="draft.minutes" (ngModelChange)="touchDraft()" required /></label>
            <label>Role <input name="role" [(ngModel)]="draft.role" (ngModelChange)="touchDraft()" placeholder="Staff, Manager, Partner" required /></label>
            <label>Activity <input name="activity" [(ngModel)]="draft.activity" (ngModelChange)="touchDraft()" placeholder="Fieldwork, Review, Planning" required /></label>
            <label><span><input type="checkbox" name="billable" [(ngModel)]="draft.billable" (ngModelChange)="touchDraft()" /> Billable to client (optional)</span></label>
            <label>Narrative (optional) <textarea name="narrative" [(ngModel)]="draft.narrative" (ngModelChange)="touchDraft()" rows="2" maxlength="2000"></textarea></label>
            <button matButton="filled" type="submit" [disabled]="cmd.busy() || cmd.uncertain()">Save time draft</button>
          </form>
        }
      </section>
      <section class="panel" aria-labelledby="timer-heading">
        <h2 id="timer-heading">Time tracker</h2>
        <p>Foreground only. Paused time is excluded. Each stopped interval must be reviewed and saved as its own draft; nothing is submitted or approved automatically.</p>
        <label>Open work task to track
          <select name="trackerTask" [ngModel]="trackerTask()" (ngModelChange)="trackerTask.set($event)" [disabled]="tracker().phase !== 'IDLE'">
            <option value="">Choose a task</option>
            @for (t of v.openTasks; track t.id) { <option [value]="t.id">{{ t.title }} ({{ t.status }})</option> }
          </select>
        </label>
        @if (tracker().phase === 'RUNNING') { <p role="timer" aria-live="off">Active time {{ elapsedText() }}</p> }
        @if (tracker().phase === 'PAUSED') { <p role="status">Timer paused · active time {{ elapsedText() }}</p> }
        @if (tracker().phase === 'STOPPED') { <p role="status">Timer stopped · active time {{ elapsedText() }}</p> }
        @if (timerNotice()) { <p role="alert">{{ timerNotice() }}</p> }
        <div class="timer-controls">
          @if (tracker().phase === 'IDLE') { <button matButton="filled" type="button" (click)="startTimer(v)" [disabled]="!canStartTimer(v)">Start</button> }
          @if (tracker().phase === 'RUNNING') { <button matButton type="button" (click)="pauseTimer()">Pause</button><button matButton="filled" type="button" (click)="stopTimer()">Stop</button> }
          @if (tracker().phase === 'PAUSED') { <button matButton="filled" type="button" (click)="resumeTimer()" [disabled]="!canResumeTimer()">Resume</button><button matButton type="button" (click)="stopTimer()">Stop</button> }
          @if (tracker().phase !== 'IDLE') { <button matButton type="button" (click)="discardTimer()" [disabled]="cmd.busy()">Discard timer</button> }
        </div>
        @if (tracker().phase === 'STOPPED') {
          @let review = timerReview();
          @if (review.rows.length) {
            <p>Review every segment before saving. Partial minutes excluded: {{ excludedSeconds(review.excludedMilliseconds) }} seconds.</p>
            <div class="table-scroll"><table>
              <caption>Stopped timer segments</caption>
              <thead><tr><th scope="col">Segment</th><th scope="col">Work date (UTC)</th><th scope="col">Start (UTC)</th><th scope="col">Minutes</th><th scope="col">State</th><th scope="col">Action</th></tr></thead>
              <tbody>@for (segment of review.rows; track segment.segment) {
                <tr><td>{{ segment.segment }}</td><td>{{ segment.workDate }}</td><td>{{ formatStartTime(segment.startMinute) }}</td><td>{{ segment.durationMinutes }}</td>
                  <td>{{ savedSegments().has(segment.segment) ? 'Saved' : unconfirmedSegment() === segment.segment ? 'Unconfirmed' : 'Needs review' }}</td>
                  <td><button matButton type="button" (click)="useSegment(segment)" [disabled]="savedSegments().has(segment.segment) || cmd.busy() || cmd.uncertain()">Use segment {{ segment.segment }}</button></td></tr>
              }</tbody>
            </table></div>
            @if (unconfirmedSegment() !== null) {
              <p role="alert">The save outcome is unconfirmed. Review recorded time before making another entry.</p>
              <button matButton type="button" (click)="refreshRecordedTime()" [disabled]="view.loading()">Refresh recorded time</button>
            }
          } @else {
            <p>No complete minute is available. Enter a manual draft if required.</p>
            @if (review.excludedMilliseconds > 0) { <p>Partial minutes excluded: {{ excludedSeconds(review.excludedMilliseconds) }} seconds.</p> }
          }
        }
      </section>
      <section class="panel" aria-labelledby="my-time-heading">
        <h2 id="my-time-heading">My recorded time</h2>
        <div class="table-scroll"><table>
          <caption>My recorded time entries</caption>
          <thead><tr><th scope="col">Date</th><th scope="col">Task</th><th scope="col">FSLI / approved mapping</th><th scope="col">Duration</th><th scope="col">Classification</th><th scope="col">Status</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
          <tbody>@for (e of v.myEntries; track e.id) {
            <tr><td>{{ e.workDate }}</td><td>{{ e.taskTitle }}</td><td>{{ e.fsliCode ?? '—' }}@if (e.mappingVersionId) { <small> · <code>{{ e.mappingVersionId }}</code></small> }</td><td>{{ e.durationMinutes }}m ({{ (e.durationMinutes / 60).toFixed(1) }}h)</td><td>{{ e.billableClassification }}</td>
              <td><audit-status [value]="e.status" /></td>
              <td>@if (e.status === 'DRAFT') { <button matButton (click)="act(e.id, 'submit', 'Time entry was submitted for approval.')" [disabled]="cmd.busy()">Submit</button> }</td></tr>
          } @empty { <tr><td colspan="7">You have not recorded any time entries in the current firm scope.</td></tr> }</tbody>
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
            <thead><tr><th scope="col">Date</th><th scope="col">Staff user</th><th scope="col">Task</th><th scope="col">FSLI / approved mapping</th><th scope="col">Duration</th><th scope="col">Activity</th><th scope="col">Narrative</th><th scope="col"><span class="sr-only">Action</span></th></tr></thead>
            <tbody>@for (e of v.awaitingApproval; track e.id) {
              <tr><td>{{ e.workDate }}</td><td><code>{{ e.userId }}</code></td><td>{{ e.taskTitle }}</td><td>{{ e.fsliCode ?? '—' }}@if (e.mappingVersionId) { <small> · <code>{{ e.mappingVersionId }}</code></small> }</td><td>{{ e.durationMinutes }}m</td><td>{{ e.activity }}</td><td>{{ e.narrative }}</td>
                <td><button matButton="filled" (click)="act(e.id, 'approve', 'Time entry was approved.')" [disabled]="cmd.busy()">Approve</button></td></tr>
            } @empty { <tr><td colspan="8">No pending submitted time entries awaiting approval.</td></tr> }</tbody>
          </table></div>
        </section>
      }
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
  styles: `:host .timer-controls { display: flex; flex-wrap: wrap; gap: .5rem; margin-block: .75rem; }`,
})
export class PracticeTime implements NavigationProtected {
  private readonly api = inject(Api);
  private readonly drafts = inject(Drafts);
  private readonly session = inject(SessionService);
  private readonly dialog = inject(MatDialog);
  readonly view = this.api.resource(() => '/api/ui/practice/time', decodeTime, 'Sign in with an authorized internal staff identity to view and record time.');
  readonly cmd = new CommandState(this.api);
  task = { title: '', period: '', engagement: '', fsli: '', due: '' };
  draft = { task: '', date: new Date().toISOString().slice(0, 10), startTime: '09:00', minutes: 60, role: 'Staff', activity: 'Fieldwork', billable: true, narrative: '' };
  readonly tracker = signal<TrackerState>(emptyTracker());
  readonly trackerTask = signal('');
  readonly clockNow = signal(Date.now());
  readonly timerNotice = signal('');
  readonly selectedSegment = signal<number | null>(null);
  readonly savedSegments = signal<ReadonlySet<number>>(new Set());
  readonly unconfirmedSegment = signal<number | null>(null);
  private readonly destroyRef = inject(DestroyRef);
  private tickTimer?: ReturnType<typeof setInterval>;
  private taskTimer?: ReturnType<typeof setTimeout>;
  private draftTimer?: ReturnType<typeof setTimeout>;
  private lastSessionInvalidation = this.session.invalidation();
  private lastSessionIdentity = this.sessionIdentity();
  private static validTask(value: unknown): PracticeTime['task'] | null {
    if (!value || typeof value !== 'object') return null;
    const v = value as Record<string, unknown>;
    return typeof v['title'] === 'string' && v['title'].length <= 200 && typeof v['period'] === 'string' &&
      typeof v['engagement'] === 'string' && typeof v['fsli'] === 'string' && typeof v['due'] === 'string'
      ? { title: v['title'], period: v['period'], engagement: v['engagement'], fsli: v['fsli'], due: v['due'] } : null;
  }
  private static validDraft(value: unknown): PracticeTime['draft'] | null {
    if (!value || typeof value !== 'object') return null;
    const v = value as Record<string, unknown>;
    return typeof v['task'] === 'string' && typeof v['date'] === 'string' && typeof v['minutes'] === 'number' &&
      typeof v['role'] === 'string' && v['role'].length <= 100 && typeof v['activity'] === 'string' && v['activity'].length <= 100 &&
      typeof v['billable'] === 'boolean' && typeof v['narrative'] === 'string' && v['narrative'].length <= 2000 &&
      (v['startTime'] === undefined || typeof v['startTime'] === 'string' && /^(?:[01][0-9]|2[0-3]):[0-5][0-9]$/.test(v['startTime']))
      ? { task: v['task'], date: v['date'], startTime: typeof v['startTime'] === 'string' ? v['startTime'] : '09:00', minutes: v['minutes'], role: v['role'], activity: v['activity'], billable: v['billable'], narrative: v['narrative'] }
      : null;
  }
  constructor() {
    const savedTask = this.drafts.load('practice-task', PracticeTime.validTask);
    if (savedTask) this.task = savedTask;
    const savedDraft = this.drafts.load('practice-time-draft', PracticeTime.validDraft);
    if (savedDraft) this.draft = savedDraft;
    effect(() => {
      const invalidation = this.session.invalidation();
      const identity = this.sessionIdentity();
      if (invalidation !== this.lastSessionInvalidation || identity !== this.lastSessionIdentity) {
        this.resetTracker();
        this.lastSessionInvalidation = invalidation;
        this.lastSessionIdentity = identity;
      }
    });
    const visibilityChanged = () => { if (document.visibilityState === 'hidden') this.pauseForExternalTrigger(); };
    const pageHidden = () => this.pauseForExternalTrigger();
    document.addEventListener('visibilitychange', visibilityChanged);
    window.addEventListener('pagehide', pageHidden);
    this.destroyRef.onDestroy(() => {
      clearTimeout(this.taskTimer); clearTimeout(this.draftTimer); this.stopTicker();
      document.removeEventListener('visibilitychange', visibilityChanged);
      window.removeEventListener('pagehide', pageHidden);
    });
  }
  touchTask(): void {
    clearTimeout(this.taskTimer);
    this.taskTimer = setTimeout(() => this.drafts.save('practice-task', this.task), 800);
  }
  selectedEngagement() {
    return this.view.data()?.engagements.find((x) => x.id === this.task.engagement) ?? null;
  }
  engagementChanged(): void {
    this.task.fsli = '';
    this.touchTask();
  }
  touchDraft(): void {
    clearTimeout(this.draftTimer);
    this.draftTimer = setTimeout(() => this.drafts.save('practice-time-draft', this.draft), 800);
  }

  private reload = () => this.view.reload();
  createTask(): void {
    const engagement = this.selectedEngagement();
    this.cmd.run('/api/ui/practice/time/tasks', { title: this.task.title, reportingPeriodId: this.task.period || null,
      dueDate: this.task.due || null, engagementId: engagement?.id ?? null,
      mappingVersionId: this.task.fsli ? engagement?.mappingVersionId ?? null : null, fsliCode: this.task.fsli || null },
      'Work task created.', () => { this.task = { title: '', period: '', engagement: '', fsli: '', due: '' }; this.drafts.clear('practice-task'); }).finally(this.reload);
  }
  async saveDraft(): Promise<void> {
    if (this.cmd.busy() || this.cmd.uncertain()) return;
    const taskId = this.draft.task || this.view.data()?.openTasks[0]?.id;
    const minutes = Number(this.draft.minutes);
    const timeMatch = /^(?:[01][0-9]|2[0-3]):[0-5][0-9]$/.exec(this.draft.startTime);
    const startMinute = timeMatch ? Number(this.draft.startTime.slice(0, 2)) * 60 + Number(this.draft.startTime.slice(3, 5)) : -1;
    if (!taskId || !Number.isSafeInteger(minutes) || minutes < 1 || minutes > 1440 || startMinute < 0 || startMinute + minutes > 1440) {
      this.cmd.failed.set(true); this.cmd.message.set('Enter a whole-minute duration and UTC start time that fit within one work date.'); return;
    }
    const generation = this.session.invalidation();
    const segment = this.selectedSegment();
    const saved = await this.cmd.run('/api/ui/practice/time/entries', { taskId, workDate: this.draft.date, startMinute, durationMinutes: minutes, role: this.draft.role,
      activity: this.draft.activity, billable: this.draft.billable, narrative: this.draft.narrative },
      `Time draft recorded (${minutes}m).`, () => { this.draft.narrative = ''; this.drafts.clear('practice-time-draft'); });
    if (generation !== this.session.invalidation()) { this.clearCommandOutcome(); return; }
    if (saved && segment !== null) {
      this.savedSegments.update(current => new Set(current).add(segment));
      if (this.unconfirmedSegment() === segment) this.unconfirmedSegment.set(null);
    } else if (!saved && this.cmd.uncertain() && segment !== null) this.unconfirmedSegment.set(segment);
    this.reload();
  }

  canStartTimer(v: ReturnType<typeof decodeTime>): boolean {
    const task = this.trackerTask();
    return !!task && v.openTasks.some(x => x.id === task) && !!this.session.current()?.staff &&
      this.tracker().phase === 'IDLE' && !this.cmd.busy() && !this.cmd.uncertain();
  }
  canResumeTimer(): boolean {
    const taskId = this.tracker().taskId;
    return !!taskId && !!this.view.data()?.openTasks.some(x => x.id === taskId) && !!this.session.current()?.staff &&
      !this.cmd.busy() && !this.cmd.uncertain();
  }
  startTimer(v: ReturnType<typeof decodeTime>): void {
    if (!this.canStartTimer(v)) return;
    const result = applyTracker(this.tracker(), { kind: 'START', taskId: this.trackerTask(), atUtcMs: Date.now() });
    if (!result.ok) { this.timerNotice.set(result.message); return; }
    this.tracker.set(result.state); this.timerNotice.set(''); this.clockNow.set(Date.now()); this.startTicker();
  }
  pauseTimer(): void { this.applyTimerAction({ kind: 'PAUSE', atUtcMs: Date.now() }); }
  resumeTimer(): void {
    if (!this.canResumeTimer()) return;
    this.applyTimerAction({ kind: 'RESUME', atUtcMs: Date.now() });
  }
  stopTimer(): void { this.applyTimerAction({ kind: 'STOP', atUtcMs: Date.now() }); }
  discardTimer(): void {
    this.stopTicker(); this.tracker.set(emptyTracker()); this.trackerTask.set(''); this.timerNotice.set('');
    this.selectedSegment.set(null); this.savedSegments.set(new Set()); this.unconfirmedSegment.set(null);
  }
  timerReview(): ReturnType<typeof draftSegments> { return draftSegments(this.tracker()); }
  elapsedText(): string {
    let milliseconds = this.tracker().intervals.reduce((sum, x) => sum + x.endUtcMs - x.startUtcMs, 0);
    const runningStart = this.tracker().phase === 'RUNNING' ? this.tracker().startedUtcMs : null;
    if (runningStart !== null) milliseconds += Math.max(0, this.clockNow() - runningStart);
    const seconds = Math.floor(milliseconds / 1000);
    return [Math.floor(seconds / 3600), Math.floor(seconds / 60) % 60, seconds % 60].map(x => x.toString().padStart(2, '0')).join(':');
  }
  formatStartTime(startMinute: number): string { return `${Math.floor(startMinute / 60).toString().padStart(2, '0')}:${(startMinute % 60).toString().padStart(2, '0')}`; }
  excludedSeconds(milliseconds: number): string {
    const seconds = milliseconds / 1000;
    return Number.isInteger(seconds) ? seconds.toString() : seconds.toFixed(3).replace(/0+$/, '').replace(/\.$/, '');
  }
  useSegment(segment: TrackerDraft): void {
    if (this.savedSegments().has(segment.segment) || this.cmd.busy() || this.cmd.uncertain()) return;
    const taskId = this.tracker().taskId;
    if (!taskId || !this.view.data()?.openTasks.some(x => x.id === taskId)) return;
    this.draft = { ...this.draft, task: taskId, date: segment.workDate, startTime: this.formatStartTime(segment.startMinute), minutes: segment.durationMinutes };
    this.selectedSegment.set(segment.segment); this.touchDraft();
  }
  refreshRecordedTime(): void { this.view.reload(); }
  async confirmNavigation(): Promise<boolean> {
    if (this.tracker().phase === 'RUNNING') this.pauseForExternalTrigger();
    if (!this.tracker().intervals.length && this.unconfirmedSegment() === null) return true;
    const choice = await firstValueFrom(this.dialog.open(PracticeTimerDiscardDialog, { disableClose: true }).afterClosed());
    if (choice === 'discard') { this.discardTimer(); return true; }
    return false;
  }
  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (!this.tracker().intervals.length && this.tracker().phase !== 'RUNNING' && this.unconfirmedSegment() === null) return;
    if (this.tracker().phase === 'RUNNING') this.pauseForExternalTrigger();
    event.preventDefault(); event.returnValue = '';
  }
  private applyTimerAction(action: { kind: 'PAUSE' | 'RESUME' | 'STOP'; atUtcMs: number }): void {
    const result = applyTracker(this.tracker(), action);
    if (!result.ok) {
      if (action.kind === 'PAUSE' || action.kind === 'STOP') this.freezeOpenInterval();
      else this.timerNotice.set(result.message);
      return;
    }
    this.tracker.set(result.state); this.timerNotice.set(''); this.clockNow.set(action.atUtcMs);
    if (result.state.phase === 'RUNNING') this.startTicker(); else this.stopTicker();
  }
  private pauseForExternalTrigger(): void {
    if (this.tracker().phase === 'RUNNING') this.applyTimerAction({ kind: 'PAUSE', atUtcMs: Date.now() });
  }
  private freezeOpenInterval(): void {
    const current = this.tracker();
    this.tracker.set({ phase: 'STOPPED', taskId: current.taskId, startedUtcMs: null, intervals: current.intervals });
    this.stopTicker(); this.clockNow.set(Date.now());
    this.timerNotice.set('Timer clock or interval limit changed. The open interval was not recorded; review completed intervals or enter time manually.');
  }
  private startTicker(): void { this.stopTicker(); this.tickTimer = setInterval(() => this.clockNow.set(Date.now()), 1000); }
  private stopTicker(): void { if (this.tickTimer) clearInterval(this.tickTimer); this.tickTimer = undefined; }
  private sessionIdentity(): string { const current = this.session.current(); return current ? `${current.firmId}:${current.userId}:${current.generation}:${current.staff}` : ''; }
  private resetTracker(): void {
    this.stopTicker(); this.tracker.set(emptyTracker()); this.trackerTask.set(''); this.timerNotice.set('');
    this.selectedSegment.set(null); this.savedSegments.set(new Set()); this.unconfirmedSegment.set(null);
  }
  private clearCommandOutcome(): void {
    this.cmd.message.set(''); this.cmd.failed.set(false); this.cmd.uncertain.set(false); this.cmd.code.set('');
  }
  act(id: string, action: 'submit' | 'approve', ok: string): void {
    this.cmd.run(`/api/ui/practice/time/entries/${id}/${action}`, {}, ok).finally(this.reload);
  }
}

@Component({
  selector: 'audit-practice-timer-discard-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `<h2 mat-dialog-title>Timer segments need review</h2>
    <mat-dialog-content><p>Leaving clears the foreground timer from this page. It does not remove any time entry already recorded.</p></mat-dialog-content>
    <mat-dialog-actions><button matButton (click)="dialog.close('keep')">Keep reviewing</button>
      <button matButton="filled" (click)="dialog.close('discard')">Discard timer and leave</button></mat-dialog-actions>`,
})
export class PracticeTimerDiscardDialog { readonly dialog = inject(MatDialogRef<PracticeTimerDiscardDialog>); }
