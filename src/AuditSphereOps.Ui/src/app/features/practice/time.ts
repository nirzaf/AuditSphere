import { afterNextRender, Component, computed, DestroyRef, ElementRef, HostListener, Injector, effect, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api, CommandState } from '../../core/api';
import { Drafts } from '../../core/drafts';
import { arr, bool, date, DecodeError, Decoder, guid, instant, nat, nullable, obj, str, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { NavigationProtected } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import { applyTracker, draftSegments, emptyTracker, TrackerDraft, TrackerState } from './time-tracker';
import { clearTimeChangeRequestsOutsideSession, decodeTimeEntryChangeProjection, hasOtherPendingTimeChange, pendingTimeChangeForSession, TimeEntryChangeAction, TimeEntryChangePanel, TimeEntryChangeSource } from './time-entry-change';

export const supportedBudgetPhases = [
  { value: 'PLANNING', label: 'Planning' },
  { value: 'FIELDWORK', label: 'Fieldwork' },
  { value: 'COMPLETION', label: 'Completion' },
  { value: 'REPORTING', label: 'Reporting' },
] as const;
const unassignedBudgetPhase = 'UNASSIGNED';
type SupportedBudgetPhase = typeof supportedBudgetPhases[number]['value'];
const isSupportedBudgetPhase = (value: unknown): value is SupportedBudgetPhase =>
  supportedBudgetPhases.some(phase => phase.value === value);

const entryShape = obj({ id: guid, workDate: date, taskId: guid, taskTitle: text, userId: guid, durationMinutes: nat, startMinute: nat, activity: text,
  billableClassification: text, narrative: text, status: text, mappingVersionId: nullable(guid), fsliCode: nullable(str(100)), phase: str(100),
  lastReturnReason: nullable(str(2000)), createdAt: nullable(instant), revision: str(19), canEdit: bool, canCorrect: bool, hasChanges: bool,
  supersedesId: nullable(guid), correctionReason: nullable(str(2000)) });
const entry: Decoder<ReturnType<typeof entryShape>> = (value, path) => {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new DecodeError(`${path || 'response'}: expected object`);
  const source = value as Record<string, unknown>;
  const changes = decodeTimeEntryChangeProjection(source, path);
  const decoded = entryShape({ ...source, ...changes, phase: source['phase'] ?? unassignedBudgetPhase,
    startMinute: source['startMinute'] ?? 540, lastReturnReason: source['lastReturnReason'] ?? null,
    createdAt: source['createdAt'] ?? null, correctionReason: source['correctionReason'] ?? null }, path);
  if (decoded.canEdit && !['DRAFT', 'RETURNED'].includes(decoded.status) || decoded.canCorrect && decoded.status !== 'APPROVED' ||
      decoded.canEdit && decoded.canCorrect) throw new DecodeError(`${path}: inconsistent time-change capability`);
  return decoded;
};
const taskOptionShape = obj({ id: guid, title: text, status: text, mappingVersionId: nullable(guid), fsliCode: nullable(str(100)), phase: str(100) });
const taskOption: Decoder<ReturnType<typeof taskOptionShape>> = (value, path) => {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new DecodeError(`${path || 'response'}: expected object`);
  const source = value as Record<string, unknown>;
  return taskOptionShape({ ...source, phase: source['phase'] ?? unassignedBudgetPhase }, path);
};
const historyCursor = obj({ workDate: date, createdAt: instant, entryId: guid });
type HistoryCursor = ReturnType<typeof historyCursor>;
const historyPageShape = obj({ pageSize: nat, hasMore: bool, nextCursor: nullable(historyCursor) });
const optionalHistoryPage = (value: unknown, path: string) => value == null ? null : historyPageShape(value, path);
// PostgreSQL timestamps can retain sub-millisecond precision. Never order a history cursor through a JS Date number.
const historyPosition = (row: { workDate: string; createdAt: string | null; entryId: string }): string => {
  const match = row.createdAt && /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)$/.exec(row.createdAt);
  if (!match || (match[2] ?? '').padEnd(7, '0')[6] !== '0' || !Number.isFinite(Date.parse(row.createdAt!))) throw new DecodeError('Invalid retained UTC entry creation time');
  return `${row.workDate}/${match[1]}.${(match[2] ?? '').padEnd(7, '0')}Z/${row.entryId.toLowerCase()}`;
};
const approvalCursor = obj({ workDate: date, entryId: guid });
type ApprovalCursor = ReturnType<typeof approvalCursor>;
const approvalPageShape = obj({ pageSize: nat, hasMore: bool, nextCursor: nullable(approvalCursor) });
const optionalApprovalPage = (value: unknown, path: string) => value == null ? null : approvalPageShape(value, path);
const timeWorkspaceShape = obj({ firmWide: bool, isApprover: bool, periods: arr(obj({ id: guid, clientId: guid, label: text }), 5000),
  engagements: arr(obj({ id: guid, clientId: guid, label: text, mappingVersionId: nullable(guid), fsliCodes: arr(str(100), 2000) }), 5000),
  openTasks: arr(taskOption, 5000), myEntries: arr(entry, 500), awaitingApproval: arr(entry, 500), approvalPage: optionalApprovalPage, entriesPage: optionalHistoryPage });
const approvalPosition = (row: { workDate: string; id: string }) => row.workDate + '/' + row.id.toLowerCase();
export const decodeTime = (value: unknown, path: string) => {
  const workspace = timeWorkspaceShape(value, path), page = workspace.approvalPage;
  if (page) {
    const rows = workspace.awaitingApproval, last = rows.at(-1), cursor = page.nextCursor;
    if (page.pageSize < 1 || page.pageSize > 100 || rows.length > page.pageSize || page.hasMore !== !!cursor ||
        (page.hasMore && (rows.length !== page.pageSize || !last || cursor!.workDate !== last.workDate || cursor!.entryId !== last.id)) ||
        rows.some((row, index) => index > 0 && approvalPosition(rows[index - 1]) >= approvalPosition(row))) {
      throw new DecodeError(`${path}: invalid approval continuation`);
    }
  }
  const history = workspace.entriesPage;
  if (history) {
    const rows = workspace.myEntries, last = rows.at(-1), cursor = history.nextCursor;
    if (history.pageSize < 1 || history.pageSize > 100 || rows.length > history.pageSize || history.hasMore !== !!cursor ||
        (history.hasMore && (rows.length !== history.pageSize || !last ||
          historyPosition(cursor!) !== historyPosition({ ...last, entryId: last.id }))) ||
        rows.some((row, index) => {
          const position = historyPosition({ ...row, entryId: row.id });
          return index > 0 && historyPosition({ ...rows[index - 1], entryId: rows[index - 1].id }) <= position;
        })) throw new DecodeError(`${path}: invalid recorded-time continuation`);
  }
  return workspace;
};

@Component({
  selector: 'audit-practice-time',
  imports: [FormsModule, MatButtonModule, MatDialogModule, RouterLink, TimeEntryChangePanel, ...SHARED],
  template: `
    <audit-page-header title="Practice time & task records" eyebrow="Work & collaboration"
      description="Time entries are recorded against assigned firm tasks with exact integer minutes, immutable rate snapshots, and independent approval segregation." />
    <audit-state [loading]="view.loading()" [error]="view.error()" label="time records" />
    @if (view.error()) { <button matButton type="button" (click)="reloadTimeWorkspace()" [disabled]="view.loading() || cmd.busy() || !!activeTimeChange()">Reload time workspace from start</button> }
    @if (view.data(); as v) {
      <p role="status" class="hint">{{ v.openTasks.length }} open tasks in scope · {{ v.myEntries.length }} of my time entries shown{{ v.isApprover ? ' · ' + v.awaitingApproval.length + ' awaiting my review shown' : '' }}</p>
      @if (timeChangeNotice()) { <p role="status" aria-live="polite">{{ timeChangeNotice() }}</p> }
      <section class="panel" aria-labelledby="task-heading">
        <h2 id="task-heading">Create work task</h2>
        <p>Scope a task to an authorized client period or engagement. Budget phase is optional. Engagement tasks can also optionally pin a destination from the current approved FSLI mapping; the exact mapping and code are copied to time entries. Due dates are never inferred.</p>
        <form class="inline-form" (submit)="$event.preventDefault(); createTask()">
          <label>Task title <input name="title" [(ngModel)]="task.title" (ngModelChange)="touchTask()" required maxlength="200" autocomplete="off" /></label>
          <label>Reporting period (optional) <select name="period" [(ngModel)]="task.period" (ngModelChange)="touchTask()">
            <option value="" [disabled]="!v.firmWide">No period link (firm-wide task)</option>
            @for (p of v.periods; track p.id) { <option [value]="p.id">{{ p.label }}</option> }</select></label>
          <label>Engagement (optional) <select name="engagement" [(ngModel)]="task.engagement" (ngModelChange)="engagementChanged()">
            <option value="">No engagement link</option>
            @for (e of v.engagements; track e.id) { <option [value]="e.id">{{ e.label }}</option> }</select></label>
          <label>Budget phase (optional) <select name="phase" [(ngModel)]="task.phase" (ngModelChange)="touchTask()">
            <option value="">No phase (UNASSIGNED)</option>
            @for (phase of budgetPhases; track phase.value) { <option [value]="phase.value">{{ phase.label }}</option> }
          </select></label>
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
        @if (taskContextNotice()) { <p role="status">{{ taskContextNotice() }}</p> }
        @if (!v.openTasks.length) { <p>No open tasks are currently available to book time against. Create or assign an open task first.</p> }
        @else {
          <form class="inline-form" (submit)="$event.preventDefault(); saveDraft()">
            <label>Work task <select name="task" [(ngModel)]="draft.task" (ngModelChange)="touchDraft()" required>@for (t of v.openTasks; track t.id) { <option [value]="t.id">{{ t.title }} ({{ t.status }}) · {{ phaseLabel(t.phase) }}{{ t.fsliCode ? ' · FSLI ' + t.fsliCode + ' · map ' + (t.mappingVersionId ?? '').slice(0, 8) : '' }}</option> }</select></label>
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
            @for (t of v.openTasks; track t.id) { <option [value]="t.id">{{ t.title }} ({{ t.status }}) · {{ phaseLabel(t.phase) }}</option> }
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
              <button matButton type="button" (click)="refreshRecordedTime()" [disabled]="view.loading() || cmd.busy()">Refresh recorded time</button>
            }
          } @else {
            <p>No complete minute is available. Enter a manual draft if required.</p>
            @if (review.excludedMilliseconds > 0) { <p>Partial minutes excluded: {{ excludedSeconds(review.excludedMilliseconds) }} seconds.</p> }
          }
        }
      </section>
      <section class="panel" aria-labelledby="my-time-heading">
        <h2 id="my-time-heading" #entriesHeading tabindex="-1">My recorded time</h2>
        <p>Newest work dates first, then creation time. This is live history; refresh from the start to include newly recorded or corrected work.</p>
        <nav class="time-pages" aria-label="Recorded time pages">
          <button matButton type="button" (click)="previousEntriesPage()" [disabled]="approvalNavigationBlocked() || entriesPositions().length === 1">Newer time entries</button>
          <span aria-live="polite">Page {{ entriesPositions().length }} · {{ v.myEntries.length }} shown</span>
          <button matButton type="button" (click)="nextEntriesPage()" [disabled]="approvalNavigationBlocked() || !v.entriesPage?.hasMore">Older time entries</button>
          <button matButton type="button" (click)="refreshEntriesQueue()" [disabled]="approvalNavigationBlocked()">Refresh recorded entries from start</button>
        </nav>
        @if (!v.entriesPage) { <p role="status">History continuation is unavailable from this server. This list may be incomplete.</p> }
        <div class="table-scroll"><table>
          <caption>My recorded time entries</caption>
          <thead><tr><th scope="col">Date</th><th scope="col">Task</th><th scope="col">Phase</th><th scope="col">FSLI / approved mapping</th><th scope="col">Duration</th><th scope="col">Classification</th><th scope="col">Status</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
          <tbody>@for (e of v.myEntries; track e.id) {
            <tr><td>{{ e.workDate }}</td><td>{{ e.taskTitle }}</td><td>{{ phaseLabel(e.phase) }}</td><td>{{ e.fsliCode ?? '—' }}@if (e.mappingVersionId) { <small> · <code>{{ e.mappingVersionId }}</code></small> }</td><td>{{ e.durationMinutes }}m ({{ (e.durationMinutes / 60).toFixed(1) }}h)</td><td>{{ e.billableClassification }}</td>
              <td><audit-status [value]="e.status" />@if (e.lastReturnReason) { <small class="return-feedback">Latest return feedback: {{ e.lastReturnReason }}</small> }</td>
              <td class="time-entry-actions">
                @if (e.status === 'DRAFT' || e.status === 'RETURNED') { <button matButton (click)="act(e.id, 'submit', e.status === 'RETURNED' ? 'Returned time was resubmitted for approval.' : 'Time entry was submitted for approval.')" [disabled]="cmd.busy() || !!activeTimeChange()">{{ e.status === 'RETURNED' ? 'Resubmit' : 'Submit' }}</button> }
                @if (e.canEdit) { <button matButton type="button" (click)="openTimeChange(e, 'EDIT_UNAPPROVED')" [disabled]="!!activeTimeChange()">Edit time</button> }
                @if (e.canCorrect) { <button matButton type="button" (click)="openTimeChange(e, 'CORRECT_APPROVED')" [disabled]="!!activeTimeChange()">Correct approved time</button> }
                <button matButton type="button" (click)="openTimeChange(e, 'HISTORY')" [disabled]="!!activeTimeChange()">Change history</button>
                @if (e.supersedesId) {
                  <small>Supersedes <code>{{ e.supersedesId }}</code></small>
                  @if (!e.hasChanges) {
                    <small>Correction reason: {{ e.correctionReason ?? 'No correction reason was retained.' }}</small>
                    <small>No historical change receipt is recorded; the corrector and before/after snapshots were not retained.</small>
                  }
                }
              </td></tr>
          } @empty { <tr><td colspan="8">No recorded time entries on this page in your current firm scope.</td></tr> }</tbody>
        </table></div>
        @if (activeTimeChange(); as active) {
          <audit-time-entry-change [entry]="activeChangeEntry()" [requestedEntryId]="active.entryId" [action]="active.action"
            [focusOnOpen]="active.focusOnOpen" [focusOrigin]="active.focusOrigin"
            (closeRequested)="closeTimeChangePanel()" (completed)="timeEntryChangeCompleted($event)" />
        }
      </section>
      <section class="panel" aria-labelledby="rates-heading">
        <h2 id="rates-heading">Charge-out rates</h2>
        <p>A time entry captures the approved charge-out rate for its role and activity as a snapshot. Rates are governed on the <a routerLink="/app/practice/rate-cards">charge-out rates</a> page, where a rate takes effect only after a separate approver approves it.</p>
      </section>
      @if (v.isApprover) {
        <section class="panel" aria-labelledby="queue-heading">
          <h2 id="queue-heading" #approvalHeading tabindex="-1">Time approval queue</h2>
          <p>Independent approval segregation: managers and partners cannot approve their own time entries.</p>
          <p>This is a live queue, not a fixed snapshot. Refresh from the start to include newly submitted or backdated work.</p>
          <nav class="time-pages" aria-label="Time approval pages">
            <button matButton type="button" (click)="previousApprovalPage()" [disabled]="approvalNavigationBlocked() || approvalPositions().length === 1">Previous approvals</button>
            <span aria-live="polite">Page {{ approvalPositions().length }} · {{ v.awaitingApproval.length }} shown</span>
            <button matButton type="button" (click)="nextApprovalPage()" [disabled]="approvalNavigationBlocked() || !v.approvalPage?.hasMore">Next approvals</button>
            <button matButton type="button" (click)="refreshApprovalQueue()" [disabled]="approvalNavigationBlocked()">Refresh approvals from start</button>
          </nav>
          @if (!v.approvalPage) { <p role="status">Continuation is unavailable from this server. This list may be incomplete.</p> }
          @if (returningTimeId()) {
            <p role="status">Finish or cancel the open return before changing approval pages.</p>
            <button matButton type="button" (click)="cancelReturn()" [disabled]="cmd.busy()">Cancel open return</button>
          }
          <div class="table-scroll"><table>
            <caption>Time entries awaiting my approval</caption>
            <thead><tr><th scope="col">Date</th><th scope="col">Staff user</th><th scope="col">Task</th><th scope="col">Phase</th><th scope="col">FSLI / approved mapping</th><th scope="col">Duration</th><th scope="col">Activity</th><th scope="col">Narrative</th><th scope="col"><span class="sr-only">Action</span></th></tr></thead>
            <tbody>@for (e of v.awaitingApproval; track e.id) {
              <tr><td>{{ e.workDate }}</td><td><code>{{ e.userId }}</code></td><td>{{ e.taskTitle }}</td><td>{{ phaseLabel(e.phase) }}</td><td>{{ e.fsliCode ?? '—' }}@if (e.mappingVersionId) { <small> · <code>{{ e.mappingVersionId }}</code></small> }</td><td>{{ e.durationMinutes }}m</td><td>{{ e.activity }}</td><td>{{ e.narrative }}</td>
                <td>
                  <button matButton="filled" (click)="act(e.id, 'approve', 'Time entry was approved.')" [disabled]="cmd.busy() || !!returningTimeId() || !!activeTimeChange()">Approve</button>
                  @if (returningTimeId() === e.id) {
                    <label>Return reason * <textarea [name]="'return-reason-' + e.id" [ngModel]="timeReturnReason()" (ngModelChange)="timeReturnReason.set($event)" maxlength="2000" required></textarea></label>
                    <button matButton type="button" (click)="returnTime(e.id)" [disabled]="cmd.busy() || !timeReturnReason().trim()">Return time</button>
                    <button matButton type="button" (click)="cancelReturn()" [disabled]="cmd.busy()">Cancel</button>
                  } @else { <button matButton type="button" (click)="beginReturn(e.id)" [disabled]="cmd.busy()">Return with reason</button> }
                </td></tr>
            } @empty { <tr><td colspan="9">No submitted time entries on this approval page. Refresh from the start for new or changed work.</td></tr> }</tbody>
          </table></div>
        </section>
      }
    }
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
  styles: `:host .time-pages { flex-wrap: wrap; } :host .time-pages button { max-width: 100%; white-space: normal; height: auto; min-height: 2.75rem; } :host .timer-controls { display: flex; flex-wrap: wrap; gap: .5rem; margin-block: .75rem; } :host .return-feedback { display: block; max-width: 32rem; white-space: normal; } :host textarea { display: block; max-width: 24rem; min-width: 16rem; } :host .time-entry-actions { min-width: 14rem; }`,
})
export class PracticeTime implements NavigationProtected {
  private readonly api = inject(Api);
  private readonly drafts = inject(Drafts);
  /** A task named by a My Work link; preselected only when the server lists it as open for this user. */
  private readonly linkedTaskId = inject(ActivatedRoute, { optional: true })?.snapshot?.queryParamMap.get('taskId')?.toLowerCase() ?? null;
  private linkedTaskResolved = false;
  readonly taskContextNotice = signal('');
  private readonly session = inject(SessionService);
  private readonly dialog = inject(MatDialog);
  readonly entriesPositions = signal<readonly (HistoryCursor | null)[]>([null]);
  private readonly entriesCursor = computed(() => this.entriesPositions().at(-1) ?? null);
  private readonly entriesHeading = viewChild<ElementRef<HTMLElement>>('entriesHeading');
  readonly approvalPositions = signal<readonly (ApprovalCursor | null)[]>([null]);
  private readonly approvalCursor = computed(() => this.approvalPositions().at(-1) ?? null);
  private readonly approvalHeading = viewChild<ElementRef<HTMLElement>>('approvalHeading');
  private readonly changePanel = viewChild(TimeEntryChangePanel);
  readonly activeTimeChange = signal<{ entryId: string; action: TimeEntryChangeAction; focusOnOpen: boolean; focusOrigin: HTMLElement | null } | null>(null);
  readonly activeChangeEntry = computed(() => {
    const active = this.activeTimeChange();
    return active ? this.view.data()?.myEntries.find(row => row.id === active.entryId) ?? null : null;
  });
  readonly timeChangeNotice = signal('');
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly renderInjector = inject(Injector);
  private approvalFocus: { target: 'approval' | 'entries'; owner: string; position: string; active: Element | null; interaction: number } | null = null;
  private focusInteraction = 0;
  readonly view = this.api.resource(() => {
    const cursor = this.approvalCursor(), history = this.entriesCursor(), query = new URLSearchParams();
    if (cursor) {
      query.set('approvalAfterWorkDate', cursor.workDate); query.set('approvalAfterEntryId', cursor.entryId); query.set('approvalPageSize', '50');
    }
    if (history) {
      query.set('entriesBeforeWorkDate', history.workDate); query.set('entriesBeforeCreatedAt', history.createdAt);
      query.set('entriesBeforeEntryId', history.entryId); query.set('entriesPageSize', '50');
    }
    return '/api/ui/practice/time' + (query.size ? '?' + query.toString() : '');
  }, (raw, path) => {
    const workspace = decodeTime(raw, path), cursor = this.approvalCursor();
    if (cursor && (!workspace.approvalPage || workspace.awaitingApproval.some(row =>
      approvalPosition(row) <= cursor.workDate + '/' + cursor.entryId.toLowerCase()))) throw new DecodeError(`${path}: wrong approval page`);
    const history = this.entriesCursor();
    if (history && (!workspace.entriesPage || workspace.myEntries.some(row =>
      historyPosition({ ...row, entryId: row.id }) >= historyPosition(history)))) throw new DecodeError(`${path}: wrong recorded-time page`);
    return workspace;
  }, 'Sign in with an authorized internal staff identity to view and record time.');
  readonly cmd = new CommandState(this.api);
  readonly budgetPhases = supportedBudgetPhases;
  phaseLabel(phase: string): string { return supportedBudgetPhases.find(x => x.value === phase)?.label ?? phase; }
  task = { title: '', period: '', engagement: '', phase: '', fsli: '', due: '' };
  draft = { task: '', date: new Date().toISOString().slice(0, 10), startTime: '09:00', minutes: 60, role: 'Staff', activity: 'Fieldwork', billable: true, narrative: '' };
  readonly tracker = signal<TrackerState>(emptyTracker());
  readonly trackerTask = signal('');
  readonly clockNow = signal(Date.now());
  readonly timerNotice = signal('');
  readonly selectedSegment = signal<number | null>(null);
  readonly savedSegments = signal<ReadonlySet<number>>(new Set());
  readonly unconfirmedSegment = signal<number | null>(null);
  readonly returningTimeId = signal<string | null>(null);
  readonly timeReturnReason = signal('');
  private readonly destroyRef = inject(DestroyRef);
  private tickTimer?: ReturnType<typeof setInterval>;
  private taskTimer?: ReturnType<typeof setTimeout>;
  private draftTimer?: ReturnType<typeof setTimeout>;
  private lastSessionInvalidation = this.session.invalidation();
  private lastSessionIdentity = this.sessionIdentity();
  private static validTask(value: unknown): PracticeTime['task'] | null {
    if (!value || typeof value !== 'object') return null;
    const v = value as Record<string, unknown>;
    const phase = v['phase'] ?? '';
    return typeof v['title'] === 'string' && v['title'].length <= 200 && typeof v['period'] === 'string' &&
      typeof v['engagement'] === 'string' && (phase === '' || isSupportedBudgetPhase(phase)) &&
      typeof v['fsli'] === 'string' && typeof v['due'] === 'string'
      ? { title: v['title'], period: v['period'], engagement: v['engagement'], phase, fsli: v['fsli'], due: v['due'] } : null;
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
    effect(() => {
      const data = this.view.data();
      if (!data || !this.linkedTaskId || this.linkedTaskResolved) return;
      this.linkedTaskResolved = true;
      const task = data.openTasks.find(t => t.id.toLowerCase() === this.linkedTaskId);
      if (!task) { this.taskContextNotice.set('The linked task is not open for your time capture with your current access.'); return; }
      if (this.draft.task && this.draft.task.toLowerCase() !== task.id.toLowerCase()) {
        this.taskContextNotice.set(`A saved time draft for another task is open. Choose “${task.title}” as the work task to record time against it.`);
        return;
      }
      this.draft = { ...this.draft, task: task.id };
      this.taskContextNotice.set(`Time capture is prepared for “${task.title}”. Nothing is recorded until you save the draft.`);
    });
    const savedTask = this.drafts.load('practice-task', PracticeTime.validTask);
    if (savedTask) this.task = savedTask;
    const savedDraft = this.drafts.load('practice-time-draft', PracticeTime.validDraft);
    if (savedDraft) this.draft = savedDraft;
    effect(() => {
      const invalidation = this.session.invalidation();
      const identity = this.sessionIdentity();
      if (invalidation !== this.lastSessionInvalidation || identity !== this.lastSessionIdentity) {
        clearTimeChangeRequestsOutsideSession(this.session.current());
        this.resetTracker();
        this.approvalPositions.set([null]);
        this.entriesPositions.set([null]);
        this.approvalFocus = null;
        this.cancelReturn();
        this.lastSessionInvalidation = invalidation;
        this.lastSessionIdentity = identity;
      }
    });
    effect(() => {
      this.session.invalidation();
      const session = this.session.current();
      const data = this.view.data();
      if (!session || !data || this.activeTimeChange()) return;
      const pending = pendingTimeChangeForSession(session);
      if (!pending) return;
      const row = data.myEntries.find(entry => entry.id.toLowerCase() === pending.entryId.toLowerCase() &&
        entry.userId.toLowerCase() === session.userId.toLowerCase());
      this.approvalFocus = null;
      this.activeTimeChange.set({ entryId: pending.entryId, action: 'HISTORY', focusOnOpen: false, focusOrigin: null });
      if (!row) this.timeChangeNotice.set('An unresolved time-change request is being checked against your current access before its saved details are shown.');
    });
    effect(() => {
      const data = this.view.data();
      const ticket = this.approvalFocus;
      if (!data || !ticket) return;
      this.approvalFocus = null;
      afterNextRender(() => {
        const document = this.host.nativeElement.ownerDocument;
        if (ticket.owner !== this.approvalOwner() || ticket.position !== this.timePagePosition() || ticket.interaction !== this.focusInteraction ||
          (document.activeElement !== document.body && document.activeElement !== ticket.active)) return;
        (ticket.target === 'entries' ? this.entriesHeading() : this.approvalHeading())?.nativeElement.focus();
      }, { injector: this.renderInjector });
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

  readonly approvalNavigationBlocked = computed(() => this.view.loading() || this.cmd.busy() || !!this.returningTimeId() || !!this.activeTimeChange());
  private approvalOwner(): string { return `${this.session.invalidation()}:${this.sessionIdentity()}`; }
  private setApprovalPosition(positions: readonly (ApprovalCursor | null)[], focus: boolean): void {
    const prior = this.timePagePosition();
    this.approvalPositions.set(positions);
    this.requestTimePageFocus(focus ? 'approval' : null);
    if (prior === this.timePagePosition()) this.view.reload();
  }
  nextApprovalPage(): void {
    const page = this.view.data()?.approvalPage;
    if (this.approvalNavigationBlocked() || !this.view.data()?.isApprover || !page?.hasMore || !page.nextCursor) return;
    this.setApprovalPosition([...this.approvalPositions(), page.nextCursor], true);
  }
  previousApprovalPage(): void {
    if (this.approvalNavigationBlocked() || this.approvalPositions().length < 2) return;
    this.setApprovalPosition(this.approvalPositions().slice(0, -1), true);
  }
  refreshApprovalQueue(): void {
    if (this.approvalNavigationBlocked()) return;
    this.setApprovalPosition([null], true);
  }
  private timePagePosition(): string { return JSON.stringify([this.approvalCursor(), this.entriesCursor()]); }
  private requestTimePageFocus(target: 'approval' | 'entries' | null): void {
    this.approvalFocus = target ? { target, owner: this.approvalOwner(), position: this.timePagePosition(),
      active: this.host.nativeElement.ownerDocument.activeElement, interaction: this.focusInteraction } : null;
  }
  private setEntriesPosition(positions: readonly (HistoryCursor | null)[]): void {
    const prior = this.timePagePosition();
    this.entriesPositions.set(positions); this.requestTimePageFocus('entries');
    if (prior === this.timePagePosition()) this.view.reload();
  }
  nextEntriesPage(): void {
    const page = this.view.data()?.entriesPage;
    if (this.approvalNavigationBlocked() || !page?.hasMore || !page.nextCursor) return;
    this.setEntriesPosition([...this.entriesPositions(), page.nextCursor]);
  }
  previousEntriesPage(): void {
    if (this.approvalNavigationBlocked() || this.entriesPositions().length < 2) return;
    this.setEntriesPosition(this.entriesPositions().slice(0, -1));
  }
  refreshEntriesQueue(): void {
    if (this.approvalNavigationBlocked()) return;
    this.setEntriesPosition([null]);
  }
  reloadTimeWorkspace(): void {
    if (this.view.loading() || this.cmd.busy() || this.activeTimeChange()) return;
    this.resetTimePages();
  }
  private resetTimePages(): void {
    const prior = this.timePagePosition();
    this.entriesPositions.set([null]); this.approvalPositions.set([null]); this.approvalFocus = null;
    if (prior === this.timePagePosition()) this.view.reload();
  }
  private reload = () => {
    if (this.returningTimeId()) this.view.reload();
    else this.resetTimePages();
  };
  createTask(): Promise<boolean> {
    if (this.activeTimeChange()) return Promise.resolve(false);
    const engagement = this.selectedEngagement();
    return this.cmd.run('/api/ui/practice/time/tasks', { title: this.task.title, reportingPeriodId: this.task.period || null,
      dueDate: this.task.due || null, engagementId: engagement?.id ?? null,
      mappingVersionId: this.task.fsli ? engagement?.mappingVersionId ?? null : null, fsliCode: this.task.fsli || null,
      ...(this.task.phase ? { phase: this.task.phase } : {}) },
      'Work task created.', () => { this.task = { title: '', period: '', engagement: '', phase: '', fsli: '', due: '' }; this.drafts.clear('practice-task'); }).finally(this.reload);
  }
  async saveDraft(): Promise<void> {
    if (this.cmd.busy() || this.cmd.uncertain() || this.activeTimeChange()) return;
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
      this.tracker().phase === 'IDLE' && !this.cmd.busy() && !this.cmd.uncertain() && !this.activeTimeChange();
  }
  canResumeTimer(): boolean {
    const taskId = this.tracker().taskId;
    return !!taskId && !!this.view.data()?.openTasks.some(x => x.id === taskId) && !!this.session.current()?.staff &&
      !this.cmd.busy() && !this.cmd.uncertain() && !this.activeTimeChange();
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
  refreshRecordedTime(): void {
    if (this.view.loading() || this.cmd.busy() || this.activeTimeChange()) return;
    // Read-only save reconciliation keeps the approval page and any unsaved return reason.
    this.setEntriesPosition([null]);
  }
  openTimeChange(entry: TimeEntryChangeSource, action: TimeEntryChangeAction): void {
    if (this.activeTimeChange() || this.view.loading() || this.cmd.busy() || this.returningTimeId() || !this.session.current()?.staff ||
        entry.userId.toLowerCase() !== this.session.current()?.userId.toLowerCase()) return;
    if (action !== 'HISTORY' && hasOtherPendingTimeChange(this.session.current(), entry.id)) {
      this.timeChangeNotice.set('Another time-change request is unresolved in this session. Reopen that entry and check its receipt before starting a different change.');
      return;
    }
    if (action === 'EDIT_UNAPPROVED' && (!entry.canEdit || !['DRAFT', 'RETURNED'].includes(entry.status))) return;
    if (action === 'CORRECT_APPROVED' && (!entry.canCorrect || entry.status !== 'APPROVED')) return;
    this.timeChangeNotice.set('');
    this.approvalFocus = null;
    const active = this.host.nativeElement.ownerDocument.activeElement;
    this.activeTimeChange.set({ entryId: entry.id, action, focusOnOpen: true,
      focusOrigin: active instanceof HTMLElement ? active : null });
  }
  async closeTimeChangePanel(): Promise<void> {
    const panel = this.changePanel();
    if (panel?.isSubmitting() || panel?.hasUncertainOutcome()) return;
    if (panel?.hasUnsubmittedChanges()) {
      const choice = await firstValueFrom(this.dialog.open(PracticeTimeChangeDiscardDialog, { disableClose: true }).afterClosed());
      if (choice !== 'discard') return;
    }
    const active = this.activeTimeChange();
    if (!active) return;
    const document = this.host.nativeElement.ownerDocument;
    const focusedBeforeClose = document.activeElement;
    const owner = this.approvalOwner();
    const interaction = this.focusInteraction;
    this.activeTimeChange.set(null);
    afterNextRender(() => {
      if (owner !== this.approvalOwner() || interaction !== this.focusInteraction ||
          (document.activeElement !== focusedBeforeClose && document.activeElement !== document.body)) return;
      const target = active.focusOrigin?.isConnected ? active.focusOrigin : this.entriesHeading()?.nativeElement;
      target?.focus();
    }, { injector: this.renderInjector });
  }
  timeEntryChangeCompleted(result: { kind: 'EDIT_UNAPPROVED' | 'CORRECT_APPROVED'; sourceTimeEntryId: string; sourceStatus: string; resultTimeEntryId: string; focusEntries: boolean }): void {
    this.timeChangeNotice.set(result.kind === 'CORRECT_APPROVED'
      ? `Approved source ${result.sourceTimeEntryId} was retained as superseded and a new draft ${result.resultTimeEntryId} was created. Submission and independent approval remain separate.`
      : `Time entry ${result.sourceTimeEntryId} was revised and remains ${result.sourceStatus} until submitted separately.`);
    const prior = this.timePagePosition();
    this.activeTimeChange.set(null);
    this.entriesPositions.set([null]); this.approvalPositions.set([null]); this.approvalFocus = null;
    if (result.focusEntries) this.requestTimePageFocus('entries');
    if (prior === this.timePagePosition()) this.view.reload();
  }

  @HostListener('document:pointerdown')
  @HostListener('document:keydown')
  recordFocusInteraction(): void { this.focusInteraction++; }

  async confirmNavigation(): Promise<boolean> {
    if (this.tracker().phase === 'RUNNING') this.pauseForExternalTrigger();
    const panel = this.changePanel();
    if (panel?.isSubmitting()) return false;
    if (panel?.hasUncertainOutcome()) {
      const choice = await firstValueFrom(this.dialog.open(PracticeTimeChangePendingNavigationDialog, { disableClose: true }).afterClosed());
      if (choice !== 'leave') return false;
    } else if (panel?.hasUnsubmittedChanges()) {
      const choice = await firstValueFrom(this.dialog.open(PracticeTimeChangeDiscardDialog, { disableClose: true }).afterClosed());
      if (choice !== 'discard') return false;
    }
    if (!this.tracker().intervals.length && this.unconfirmedSegment() === null) return true;
    const choice = await firstValueFrom(this.dialog.open(PracticeTimerDiscardDialog, { disableClose: true }).afterClosed());
    if (choice === 'discard') { this.discardTimer(); return true; }
    return false;
  }
  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (!this.tracker().intervals.length && this.tracker().phase !== 'RUNNING' && this.unconfirmedSegment() === null &&
        !this.changePanel()?.needsNavigationWarning()) return;
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
  act(id: string, action: 'submit' | 'approve', ok: string): Promise<boolean> {
    if (this.activeTimeChange() || action === 'approve' && this.returningTimeId()) return Promise.resolve(false);
    return this.cmd.run(`/api/ui/practice/time/entries/${id}/${action}`, {}, ok).finally(this.reload);
  }
  beginReturn(id: string): void { if (!this.activeTimeChange()) { this.returningTimeId.set(id); this.timeReturnReason.set(''); } }
  cancelReturn(): void { this.returningTimeId.set(null); this.timeReturnReason.set(''); }
  returnTime(id: string): Promise<boolean> | void {
    const reason = this.timeReturnReason().trim();
    if (this.activeTimeChange() || this.returningTimeId() !== id || !reason || reason.length > 2000 || this.cmd.busy() || this.cmd.uncertain()) return;
    return this.cmd.run(`/api/ui/practice/time/entries/${id}/return`, { reason }, 'Time entry was returned to its preparer with the recorded reason.',
      () => this.cancelReturn()).finally(this.reload);
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

@Component({
  selector: 'audit-practice-time-change-discard-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `<h2 mat-dialog-title>Unsubmitted time changes</h2>
    <mat-dialog-content><p>Your edits have not been sent. Discard them before leaving this time entry?</p></mat-dialog-content>
    <mat-dialog-actions><button matButton (click)="dialog.close('keep')">Keep editing</button>
      <button matButton="filled" (click)="dialog.close('discard')">Discard unsent changes</button></mat-dialog-actions>`,
})
export class PracticeTimeChangeDiscardDialog { readonly dialog = inject(MatDialogRef<PracticeTimeChangeDiscardDialog>); }

@Component({
  selector: 'audit-practice-time-change-pending-navigation-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `<h2 mat-dialog-title>Time change outcome is unresolved</h2>
    <mat-dialog-content><p>The exact request and values are retained in this browser tab. You can leave and recover the same request later from this entry, but must not create a different change first.</p></mat-dialog-content>
    <mat-dialog-actions><button matButton (click)="dialog.close('stay')">Stay and recover now</button>
      <button matButton="filled" (click)="dialog.close('leave')">Leave and recover later</button></mat-dialog-actions>`,
})
export class PracticeTimeChangePendingNavigationDialog { readonly dialog = inject(MatDialogRef<PracticeTimeChangePendingNavigationDialog>); }
