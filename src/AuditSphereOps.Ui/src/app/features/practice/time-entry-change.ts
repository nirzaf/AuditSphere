import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { afterNextRender, Component, DestroyRef, ElementRef, computed, effect, HostListener, inject, Injector, input, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { firstValueFrom, timeout } from 'rxjs';
import { Api } from '../../core/api';
import { arr, bool, date, DecodeError, decode, Decoder, guid, instant, nat, nullable, obj, sha256, str, text } from '../../core/decode';
import { Session, SessionService } from '../../core/session';

export type TimeEntryChangeKind = 'EDIT_UNAPPROVED' | 'CORRECT_APPROVED';
export type TimeEntryChangeAction = TimeEntryChangeKind | 'HISTORY';

export interface TimeEntryChangeSource {
  id: string;
  userId: string;
  workDate: string;
  startMinute: number;
  durationMinutes: number;
  activity: string;
  narrative: string;
  status: string;
  revision: string;
  canEdit: boolean;
  canCorrect: boolean;
  hasChanges: boolean;
  supersedesId: string | null;
  correctionReason?: string | null;
}

export interface TimeEntryChangeValues {
  workDate: string;
  startMinute: number;
  durationMinutes: number;
  activity: string;
  narrative: string;
}

export interface TimeEntryChangeRequest {
  requestId: string;
  expectedRevision: string;
  kind: TimeEntryChangeKind;
  workDate: string;
  startMinute: number;
  durationMinutes: number;
  activity: string;
  narrative: string;
  reason: string;
}

export interface TimeEntryChangeReceipt {
  id: string;
  actorId: string;
  actorName: string | null;
  actorEpoch: string;
  requestId: string;
  requestHash: string;
  kind: TimeEntryChangeKind;
  timeEntryId: string;
  resultTimeEntryId: string;
  sourceRevision: string;
  resultRevision: string;
  reason: string | null;
  createdAt: string;
  before: TimeEntryChangeValues;
  after: TimeEntryChangeValues;
  sourceStatusBefore: string;
  sourceStatusAfter: string;
  resultStatus: string;
}

export interface TimeEntryChangePage {
  items: TimeEntryChangeReceipt[];
  pageSize: number;
  hasMore: boolean;
  nextCursor: string | null;
}

export interface TimeEntryChangeLookup {
  found: boolean;
  receipt: TimeEntryChangeReceipt | null;
}

const int64Maximum = 9223372036854775807n;
export const int64String: Decoder<string> = (value, path) => {
  if (typeof value !== 'string' || !/^(?:0|[1-9][0-9]{0,18})$/.test(value))
    throw new DecodeError(`${path || 'response'}: expected non-negative Int64 decimal string`);
  if (BigInt(value) > int64Maximum) throw new DecodeError(`${path || 'response'}: Int64 is out of range`);
  return value;
};

export interface TimeEntryChangeProjection {
  revision: string;
  canEdit: boolean;
  canCorrect: boolean;
  hasChanges: boolean;
  supersedesId: string | null;
}

export function decodeTimeEntryChangeProjection(value: Record<string, unknown>, path: string): TimeEntryChangeProjection {
  const at = (field: string) => path ? `${path}.${field}` : field;
  const revision = value['revision'] === undefined ? '1' : int64String(value['revision'], at('revision'));
  if (revision === '0') throw new DecodeError(`${at('revision')}: expected positive revision`);
  const canEdit = value['canEdit'] === undefined ? false : bool(value['canEdit'], at('canEdit'));
  const canCorrect = value['canCorrect'] === undefined ? false : bool(value['canCorrect'], at('canCorrect'));
  const hasChanges = value['hasChanges'] === undefined ? false : bool(value['hasChanges'], at('hasChanges'));
  const supersedesId = value['supersedesId'] === undefined ? null : nullable(guid)(value['supersedesId'], at('supersedesId'));
  return { revision, canEdit, canCorrect, hasChanges, supersedesId };
}

const changeKind = (value: unknown, path: string): TimeEntryChangeKind =>
  value === 'EDIT_UNAPPROVED' || value === 'CORRECT_APPROVED'
    ? value : (() => { throw new DecodeError(`${path || 'response'}: expected time change kind`); })();
const snapshotValuesShape = obj({ workDate: date, startMinute: nat, durationMinutes: nat, activity: str(64000), narrative: str(64000) });
const requestShape = obj({ requestId: guid, expectedRevision: int64String, kind: changeKind,
  workDate: date, startMinute: nat, durationMinutes: nat, activity: str(20000), narrative: str(2000), reason: str(2000) });
const requestDecoder: Decoder<ReturnType<typeof requestShape>> = (value, path) => {
  const request = requestShape(value, path);
  if (request.expectedRevision === '0') throw new DecodeError(`${path || 'response'}.expectedRevision: expected positive revision`);
  return request;
};
const receiptShape = obj({ id: guid, actorId: guid, actorName: nullable(str(300)), actorEpoch: int64String,
  requestId: guid, requestHash: sha256, kind: changeKind, timeEntryId: guid, resultTimeEntryId: guid,
  sourceRevision: int64String, resultRevision: int64String, reason: nullable(str(2000)), createdAt: instant,
  before: snapshotValuesShape, after: snapshotValuesShape, sourceStatusBefore: text, sourceStatusAfter: text, resultStatus: text });
const receiptDecoder: Decoder<ReturnType<typeof receiptShape>> = (value, path) => {
  const receipt = receiptShape(value, path);
  const sourceRevision = BigInt(receipt.sourceRevision);
  if (sourceRevision < 1n || sourceRevision >= int64Maximum || BigInt(receipt.resultRevision) !== sourceRevision + 1n)
    throw new DecodeError(`${path || 'response'}: inconsistent time-change revisions`);
  const validEdit = receipt.kind === 'EDIT_UNAPPROVED' &&
    ['DRAFT', 'RETURNED'].includes(receipt.sourceStatusBefore) &&
    receipt.sourceStatusAfter === receipt.sourceStatusBefore && receipt.resultStatus === receipt.sourceStatusBefore &&
    receipt.resultTimeEntryId.toLowerCase() === receipt.timeEntryId.toLowerCase();
  const validCorrection = receipt.kind === 'CORRECT_APPROVED' && receipt.sourceStatusBefore === 'APPROVED' &&
    receipt.sourceStatusAfter === 'SUPERSEDED' && receipt.resultStatus === 'DRAFT' &&
    receipt.resultTimeEntryId.toLowerCase() !== receipt.timeEntryId.toLowerCase();
  if (!validEdit && !validCorrection) throw new DecodeError(`${path || 'response'}: inconsistent time-change outcome`);
  return receipt;
};
const pageShape = obj({ items: arr(receiptDecoder, 20), pageSize: nat, hasMore: bool, nextCursor: nullable(guid) });
const lookupShape = obj({ found: bool, receipt: nullable(receiptDecoder) });
const lookupDecoder: Decoder<TimeEntryChangeLookup> = (value, path) => {
  const lookup = lookupShape(value, path);
  if (lookup.found !== (lookup.receipt !== null)) throw new DecodeError(`${path || 'response'}: inconsistent receipt lookup`);
  return lookup;
};
const pendingSourceShape = obj({ revision: int64String, status: text, values: snapshotValuesShape });
const storedPendingShape = obj({ contextKey: text, timeEntryId: guid, request: requestDecoder, source: pendingSourceShape });

type StoredPendingChange = ReturnType<typeof storedPendingShape>;
interface TimeChangeFocusIntent {
  context: string;
  actorScope: string;
  generation: number;
  interaction: number;
  active: Element | null;
}

const equalValues = (left: TimeEntryChangeValues, right: TimeEntryChangeValues): boolean =>
  left.workDate === right.workDate && left.startMinute === right.startMinute && left.durationMinutes === right.durationMinutes &&
  left.activity === right.activity && left.narrative === right.narrative;
const valuesFor = (request: TimeEntryChangeRequest): TimeEntryChangeValues => ({ workDate: request.workDate,
  startMinute: request.startMinute, durationMinutes: request.durationMinutes, activity: request.activity, narrative: request.narrative });
const sourceValuesFor = (source: TimeEntryChangeSource): TimeEntryChangeValues => ({ workDate: source.workDate,
  startMinute: source.startMinute, durationMinutes: source.durationMinutes, activity: source.activity, narrative: source.narrative });
const actorScopeFor = (session: Session | null): string => session?.staff
  ? `${encodeURIComponent(session.firmId)}:${encodeURIComponent(session.userId)}:${encodeURIComponent(session.generation)}` : '';
const sameGuid = (left: string, right: string): boolean => left.toLowerCase() === right.toLowerCase();
const pendingPrefix = 'auditsphere-time-entry-change:v1:';
const pendingKey = (contextKey: string): string => `${pendingPrefix}${contextKey}`;

export function clearTimeChangeRequestsOutsideSession(session: Session | null): void {
  try {
    if (typeof sessionStorage === 'undefined') return;
    const allowedPrefix = session?.staff ? `${pendingPrefix}${actorScopeFor(session)}:` : '';
    for (let index = sessionStorage.length - 1; index >= 0; index--) {
      const key = sessionStorage.key(index);
      if (key?.startsWith(pendingPrefix) && (!allowedPrefix || !key.startsWith(allowedPrefix))) sessionStorage.removeItem(key);
    }
  } catch { /* Storage may be unavailable. */ }
}

export function pendingTimeChangeForSession(session: Session | null): { entryId: string; kind: TimeEntryChangeKind | null } | null {
  try {
    if (!session?.staff || typeof sessionStorage === 'undefined') return null;
    const prefix = `${pendingPrefix}${actorScopeFor(session)}:`;
    for (let index = 0; index < sessionStorage.length; index++) {
      const key = sessionStorage.key(index);
      if (!key?.startsWith(prefix)) continue;
      const keyEntryId = key.slice(prefix.length);
      try {
        const stored = decode(storedPendingShape, JSON.parse(sessionStorage.getItem(key) ?? 'null'));
        if (stored.contextKey !== key.slice(pendingPrefix.length) || stored.timeEntryId.toLowerCase() !== keyEntryId.toLowerCase())
          return { entryId: keyEntryId, kind: null };
        return { entryId: stored.timeEntryId, kind: stored.request.kind };
      } catch { return { entryId: keyEntryId, kind: null }; }
    }
  } catch { /* A malformed saved request remains locked in its own scoped panel. */ }
  return null;
}

export function hasOtherPendingTimeChange(session: Session | null, entryId: string): boolean {
  try {
    if (!session?.staff || typeof sessionStorage === 'undefined') return false;
    const prefix = `${pendingPrefix}${actorScopeFor(session)}:`;
    for (let index = 0; index < sessionStorage.length; index++) {
      const key = sessionStorage.key(index);
      if (key?.startsWith(prefix) && key.slice(prefix.length).toLowerCase() !== entryId.toLowerCase()) return true;
    }
  } catch { return true; }
  return false;
}

@Component({
  selector: 'audit-time-entry-change',
  imports: [FormsModule, MatButtonModule],
  template: `
    <section class="panel time-change" aria-labelledby="time-change-heading">
      @if (privateUnavailable()) {
        <div class="heading-row"><h3 #panelHeading id="time-change-heading" tabindex="-1">Time entry change unavailable</h3>
          <button matButton type="button" (click)="closeRequested.emit()" [disabled]="busy() || uncertain()">Close time change</button></div>
        <p role="alert">This time entry is unavailable in your current identity or scope. Its private change details were cleared.</p>
      } @else if (source(); as entry) {
        <div class="heading-row">
          <h3 #panelHeading id="time-change-heading" tabindex="-1">Time entry change · {{ entry.workDate }}</h3>
          <button matButton type="button" (click)="closeRequested.emit()" [disabled]="busy() || uncertain()">Close time change</button>
        </div>
        <p>Changes never submit or approve time. Returned work remains returned until you resubmit it; an approved correction creates a separate draft for independent review.</p>
        @if (uncertain()) {
          <section class="recovery" aria-labelledby="time-change-recovery-heading">
            <h4 #recoveryHeading id="time-change-recovery-heading" tabindex="-1">Change outcome needs review</h4>
            <p>{{ recoveryMessage() }}</p>
            <p>The original request values and identity are frozen. Do not enter a different change while this request is unresolved.</p>
            @if (receiptState() === 'NOT_FOUND') {
              @if (pendingRequest(); as request) {
                <dl class="recovery-values"><dt>Saved work date</dt><dd>{{ request.workDate }}</dd>
                  <dt>Saved start time</dt><dd>{{ startTime(request.startMinute) }} UTC</dd>
                  <dt>Saved duration</dt><dd>{{ request.durationMinutes }} minutes</dd>
                  <dt>Saved activity</dt><dd>{{ request.activity }}</dd><dt>Saved narrative</dt><dd>{{ request.narrative || 'No narrative' }}</dd>
                  <dt>Saved reason</dt><dd>{{ request.reason }}</dd></dl>
              }
            }
            @if (recoveryError()) { <p role="alert">{{ recoveryError() }}</p> }
            <button matButton type="button" (click)="checkSavedChange()" [disabled]="busy() || !hasPendingRequest()">{{ busy() ? 'Checking saved change…' : 'Check saved change receipt' }}</button>
            @if (receiptState() === 'MISMATCH' && !hasPendingRequest()) { <p role="alert">Saved recovery data is unreadable. Do not submit another change; inspect retained history or contact your administrator.</p> }
            @if (receiptState() === 'NOT_FOUND') {
              <button matButton="filled" type="button" (click)="retryExactChange()" [disabled]="busy()">Retry the exact saved change</button>
            }
          </section>
        } @else if (editorOpen() && !reviewing()) {
          <form class="change-form" (submit)="$event.preventDefault(); reviewChange()">
            <h4>{{ action() === 'EDIT_UNAPPROVED' ? 'Edit this draft or returned entry' : 'Correct approved time into a new draft' }}</h4>
            <label>Work date (UTC) <input #changeWorkDate name="changeWorkDate" type="date" [(ngModel)]="form.workDate" required /></label>
            <label>Start time (UTC) <input name="changeStartTime" type="time" [(ngModel)]="form.startTime" required /></label>
            <label>Duration (minutes) <input name="changeDuration" type="number" min="1" max="1440" step="1" [(ngModel)]="form.durationMinutes" required /></label>
            <label>Activity <input name="changeActivity" [(ngModel)]="form.activity" maxlength="20000" required /></label>
            <label>Narrative (optional) <textarea name="changeNarrative" [(ngModel)]="form.narrative" rows="3" maxlength="2000"></textarea></label>
            @if (form.activity.length > 20000 || form.narrative.length > 2000) {
              <p role="alert">A retained value exceeds the new change-request limit. Shorten it explicitly before saving; the original text was not truncated.</p>
            }
            <label>Reason for this change * <textarea name="changeReason" [(ngModel)]="form.reason" rows="3" maxlength="2000" required></textarea></label>
            @if (formError()) { <p role="alert">{{ formError() }}</p> }
            <button matButton="filled" type="submit" [disabled]="busy() || !formIsValid()">Review exact change</button>
          </form>
        } @else if (editorOpen() && reviewing()) {
          <section aria-labelledby="time-change-review-heading">
            <h4 #reviewHeading id="time-change-review-heading" tabindex="-1">Review before saving</h4>
            <p>Source entry <code>{{ entry.id }}</code> · revision {{ entry.revision }} · current status {{ entry.status }}.</p>
            <dl><dt>Work date</dt><dd>{{ form.workDate }}</dd><dt>Start (UTC)</dt><dd>{{ form.startTime }}</dd>
              <dt>Duration</dt><dd>{{ form.durationMinutes }} minutes</dd><dt>Activity</dt><dd>{{ form.activity.trim() }}</dd>
              <dt>Narrative</dt><dd>{{ form.narrative.trim() || 'No narrative' }}</dd><dt>Reason</dt><dd>{{ form.reason.trim() }}</dd></dl>
            <p>{{ action() === 'EDIT_UNAPPROVED' ? 'The entry keeps its current draft or returned status and submission round.' : 'The approved source remains in history; this action creates a new draft and does not submit it.' }}</p>
            <button matButton type="button" (click)="backToEdit()" [disabled]="busy()">Back to edit</button>
            <button matButton="filled" type="button" (click)="saveReviewedChange()" [disabled]="busy()">{{ busy() ? 'Saving…' : action() === 'EDIT_UNAPPROVED' ? 'Save time change' : 'Create corrected draft' }}</button>
          </section>
        } @else if (confirmedReceipt()) {
          <p role="status">The saved change receipt was confirmed. Refreshing the time entry list.</p>
        } @else if (accessUnavailable()) {
          <p role="alert">Change details are unavailable in your current scope. Private values were cleared.</p>
        } @else if (action() === 'EDIT_UNAPPROVED' && entry.canEdit) {
          <p role="status">Edit the recorded values and provide a reason. The entry will remain {{ entry.status.toLowerCase() }} until submitted separately.</p>
        } @else if (action() === 'CORRECT_APPROVED' && entry.canCorrect) {
          <p role="status">Provide corrected values and a reason. A successful correction creates a new draft; the approved entry is not edited or reapproved.</p>
        }

        @if (error()) { <p role="alert">{{ error() }}</p> }
        <section class="change-history" aria-labelledby="time-change-history-heading">
          <h4 id="time-change-history-heading">Retained change history</h4>
          @if (historyLoading()) { <p role="status">Loading retained change history…</p> }
          @if (historyError()) {
            <p role="status">{{ historyError() }}</p>
            <button matButton type="button" (click)="loadHistoryFromStart()" [disabled]="busy() || historyLoading()">Retry change history</button>
          } @else if (currentHistory(); as page) {
            @if (page.items.length) {
              <nav aria-label="Time change history pages">
                <button matButton type="button" (click)="previousHistoryPage()" [disabled]="busy() || historyPages().length < 2">Newer changes</button>
                <span aria-live="polite">Page {{ historyPages().length }} · {{ page.items.length }} changes</span>
                <button matButton type="button" (click)="nextHistoryPage()" [disabled]="busy() || !page.hasMore">Older changes</button>
              </nav>
              <ol>
                @for (change of page.items; track change.id) {
                  <li>
                    <article>
                      <h5>{{ change.kind === 'EDIT_UNAPPROVED' ? 'Unapproved time revised' : 'Approved time corrected' }}</h5>
                      <p>Recorded by {{ change.actorName || 'Name not retained' }} · actor <code>{{ change.actorId }}</code> · <time [attr.datetime]="change.createdAt">{{ utcTime(change.createdAt) }}</time></p>
                      <p>Reason: {{ change.reason ?? 'No reason was retained for this legacy change.' }}</p>
                      <p>Source <code>{{ change.timeEntryId }}</code> revision {{ change.sourceRevision }} · {{ change.sourceStatusBefore }} → {{ change.sourceStatusAfter }}.</p>
                      <p>Result <code>{{ change.resultTimeEntryId }}</code> revision {{ change.resultRevision }} · {{ change.resultStatus }}.</p>
                      <div class="value-pair">
                        <section><h6>Before</h6><dl><dt>Work date</dt><dd>{{ change.before.workDate }}</dd><dt>Start</dt><dd>{{ startTime(change.before.startMinute) }} UTC</dd><dt>Duration</dt><dd>{{ change.before.durationMinutes }} minutes</dd><dt>Activity</dt><dd>{{ change.before.activity }}</dd><dt>Narrative</dt><dd>{{ change.before.narrative || 'No narrative' }}</dd></dl></section>
                        <section><h6>After</h6><dl><dt>Work date</dt><dd>{{ change.after.workDate }}</dd><dt>Start</dt><dd>{{ startTime(change.after.startMinute) }} UTC</dd><dt>Duration</dt><dd>{{ change.after.durationMinutes }} minutes</dd><dt>Activity</dt><dd>{{ change.after.activity }}</dd><dt>Narrative</dt><dd>{{ change.after.narrative || 'No narrative' }}</dd></dl></section>
                      </div>
                    </article>
                  </li>
                }
              </ol>
            } @else {
              <p>No retained change receipts are recorded for this entry. Earlier corrections are not inferred or backfilled.</p>
            }
          }
        </section>
      } @else if (uncertain() && receiptState() === 'MISMATCH') {
        <div class="heading-row"><h3 #panelHeading id="time-change-heading" tabindex="-1">Change outcome needs review</h3>
          <button matButton type="button" (click)="closeRequested.emit()" [disabled]="busy() || uncertain()">Close time change</button></div>
        <h4 #recoveryHeading tabindex="-1">Saved recovery details are unavailable</h4>
        <p role="alert">{{ recoveryError() || 'This request cannot be checked until its saved identity is validated. No entry details are shown.' }}</p>
      } @else {
        <div class="heading-row"><h3 #panelHeading id="time-change-heading" tabindex="-1">Checking time-entry access</h3>
          <button matButton type="button" (click)="closeRequested.emit()" [disabled]="busy() || uncertain()">Close time change</button></div>
        <p role="status">Checking current access for this saved change. Entry details stay hidden until access is confirmed.</p>
        @if (recoveryError()) { <p role="alert">{{ recoveryError() }}</p> }
      }
    </section>
  `,
  styles: `:host { display: block; min-width: 0; } .heading-row { display: flex; align-items: center; justify-content: space-between; gap: .75rem; min-width: 0; } .heading-row h3 { min-width: 0; overflow-wrap: anywhere; } .change-form { display: flex; flex-wrap: wrap; gap: .75rem; align-items: start; min-width: 0; } .change-form label { display: grid; gap: .25rem; min-width: min(100%, 16rem); max-width: 100%; } .change-form input, .change-form textarea { box-sizing: border-box; max-width: 100%; min-width: 0; } .value-pair { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 18rem), 1fr)); gap: 1rem; min-width: 0; } .value-pair section { min-width: 0; } .value-pair dd, .change-history p, .recovery-values dd { overflow-wrap: anywhere; white-space: pre-wrap; } .change-history code, .recovery code { overflow-wrap: anywhere; word-break: break-word; } .recovery { min-width: 0; border-inline-start: .25rem solid currentColor; padding-inline-start: 1rem; }` })
export class TimeEntryChangePanel {
  readonly entry = input<TimeEntryChangeSource | null>(null);
  readonly requestedEntryId = input.required<string>();
  readonly action = input.required<TimeEntryChangeAction>();
  readonly focusOnOpen = input(false);
  readonly focusOrigin = input<HTMLElement | null>(null);
  readonly closeRequested = output<void>();
  readonly completed = output<{ kind: TimeEntryChangeKind; sourceTimeEntryId: string; sourceStatus: string; resultTimeEntryId: string; focusEntries: boolean }>();

  private readonly api = inject(Api);
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly renderInjector = inject(Injector);
  private readonly panelHeading = viewChild<ElementRef<HTMLElement>>('panelHeading');
  private readonly reviewHeading = viewChild<ElementRef<HTMLElement>>('reviewHeading');
  private readonly recoveryHeading = viewChild<ElementRef<HTMLElement>>('recoveryHeading');
  private readonly changeWorkDate = viewChild<ElementRef<HTMLInputElement>>('changeWorkDate');

  readonly busy = signal(false);
  readonly historyLoading = signal(false);
  readonly historyError = signal('');
  readonly historyPages = signal<readonly TimeEntryChangePage[]>([]);
  readonly currentHistory = computed(() => this.historyPages().at(-1) ?? null);
  readonly uncertain = signal(false);
  readonly hasPendingRequest = signal(false);
  readonly receiptState = signal<'UNCONFIRMED' | 'NOT_FOUND' | 'FOUND' | 'MISMATCH'>('UNCONFIRMED');
  readonly recoveryMessage = signal('A previous time change may have been saved. Check its actor-owned receipt before taking another action.');
  readonly recoveryError = signal('');
  readonly error = signal('');
  readonly formError = signal('');
  readonly editorOpen = signal(false);
  readonly reviewing = signal(false);
  readonly confirmedReceipt = signal(false);
  readonly privateUnavailable = signal(false);
  readonly accessUnavailable = signal(false);
  private readonly authorizedFallback = signal<TimeEntryChangeSource | null>(null);
  readonly source = computed(() => {
    const userId = this.session.current()?.userId;
    const requestedId = this.requestedEntryId();
    const live = this.entry();
    if (live && userId && sameGuid(live.id, requestedId) && sameGuid(live.userId, userId)) return live;
    const fallback = this.authorizedFallback();
    return fallback && userId && sameGuid(fallback.id, requestedId) && sameGuid(fallback.userId, userId) ? fallback : null;
  });
  readonly pendingRequest = computed(() => { this.hasPendingRequest(); return this.pending?.request ?? null; });
  form = { workDate: '', startTime: '09:00', durationMinutes: 60, activity: '', narrative: '', reason: '' };

  private actorScope = '';
  private contextKey = '';
  private destroyed = false;
  private boundAction: TimeEntryChangeAction | null = null;
  private pending: StoredPendingChange | null = null;
  private focusInteraction = 0;
  private busyOperationToken = 0;
  private historyOperationToken = 0;

  constructor() {
    effect(() => {
      this.session.invalidation();
      const session = this.session.current();
      const row = this.entry();
      const requestedId = this.requestedEntryId();
      const action = this.action();
      const nextActorScope = session?.staff ? actorScopeFor(session) : '';
      if (nextActorScope !== this.actorScope) {
        if (this.actorScope) this.clearStoredActorScope(this.actorScope);
        this.resetPrivateState();
        this.actorScope = nextActorScope;
        this.clearOtherStoredScopes(nextActorScope);
      }
      let validRequestedId = '';
      try { validRequestedId = decode(guid, requestedId).toLowerCase(); } catch { /* Invalid recovery identities stay private. */ }
      const liveRowMatches = !!row && !!session && sameGuid(row.id, validRequestedId) && sameGuid(session.userId, row.userId);
      const offPageRecovery = !row && action === 'HISTORY' && !!validRequestedId;
      const nextContext = nextActorScope && session && validRequestedId && (liveRowMatches || offPageRecovery)
        ? `${nextActorScope}:${validRequestedId}` : '';
      if (nextContext === this.contextKey && action === this.boundAction) return;
      this.contextKey = nextContext;
      this.boundAction = action;
      this.resetEntryState();
      if (!nextContext || !session) {
        this.privateUnavailable.set(true);
        return;
      }
      this.privateUnavailable.set(false);
      this.restorePending(nextContext, validRequestedId);
      if (offPageRecovery) {
        if (!this.pending && !this.uncertain()) this.privateUnavailable.set(true);
        if (this.pending) void this.checkSavedChange(true);
        return;
      }
      if (!row) {
        this.privateUnavailable.set(true);
        return;
      }
      if (!this.pending && action !== 'HISTORY' && row) {
        const eligible = action === 'EDIT_UNAPPROVED'
          ? row.canEdit && ['DRAFT', 'RETURNED'].includes(row.status)
          : row.canCorrect && row.status === 'APPROVED';
        if (eligible) this.beginEdit(row, action);
        else this.error.set('This entry is no longer eligible for the selected change. Refresh it before editing.');
      }
      if (this.focusOnOpen() && liveRowMatches) {
        const intent = this.captureFocusIntent(nextContext, this.focusOrigin());
        this.focusAfterRender(() => this.panelHeading()?.nativeElement, intent);
      }
      void this.loadHistoryPage(null, true, nextContext);
    });
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      this.busyOperationToken++;
      this.historyOperationToken++;
      if (this.actorScope && this.actorScope !== actorScopeFor(this.session.current())) this.clearStoredActorScope(this.actorScope);
    });
  }

  hasUncertainOutcome(): boolean { return this.uncertain(); }
  hasUnsubmittedChanges(): boolean {
    if (!this.editorOpen() || this.uncertain()) return false;
    const row = this.source();
    if (!row) return false;
    const form = this.form;
    return form.reason.trim().length > 0 || form.workDate !== row.workDate || form.startTime !== this.startTime(row.startMinute) ||
      form.durationMinutes !== row.durationMinutes || form.activity !== row.activity || form.narrative !== row.narrative;
  }
  isSubmitting(): boolean { return this.busy(); }
  needsNavigationWarning(): boolean { return this.isSubmitting() || this.hasUncertainOutcome() || this.hasUnsubmittedChanges(); }

  @HostListener('document:pointerdown')
  @HostListener('document:keydown')
  recordFocusInteraction(): void { this.focusInteraction++; }

  private captureFocusIntent(context = this.contextKey, expectedOrigin?: Element | null): TimeChangeFocusIntent | null {
    if (!context) return null;
    const document = this.host.nativeElement.ownerDocument;
    const active = document.activeElement;
    if (expectedOrigin && active !== expectedOrigin && active !== document.body) return null;
    return { context, actorScope: this.actorScope, generation: this.session.invalidation(), interaction: this.focusInteraction, active };
  }
  private focusIntentIsCurrent(intent: TimeChangeFocusIntent | null): intent is TimeChangeFocusIntent {
    if (!intent || this.destroyed || intent.context !== this.contextKey || intent.actorScope !== this.actorScope ||
        intent.actorScope !== actorScopeFor(this.session.current()) || intent.generation !== this.session.invalidation() ||
        intent.interaction !== this.focusInteraction) return false;
    const requestedId = this.requestedEntryId().toLowerCase();
    if (intent.context !== `${intent.actorScope}:${requestedId}`) return false;
    const document = this.host.nativeElement.ownerDocument;
    const active = document.activeElement;
    return active === intent.active || active === document.body || active === this.panelHeading()?.nativeElement;
  }
  private focusAfterRender(target: () => HTMLElement | undefined, intent: TimeChangeFocusIntent | null): void {
    if (!intent) return;
    afterNextRender(() => {
      if (!this.focusIntentIsCurrent(intent)) return;
      target()?.focus();
    }, { injector: this.renderInjector });
  }

  private backToEdit(): void {
    const intent = this.captureFocusIntent();
    this.reviewing.set(false);
    this.focusAfterRender(() => this.changeWorkDate()?.nativeElement, intent);
  }

  private resetPrivateState(): void {
    this.pending = null;
    this.uncertain.set(false);
    this.receiptState.set('UNCONFIRMED');
    this.recoveryMessage.set('A previous time change may have been saved. Check its actor-owned receipt before taking another action.');
    this.recoveryError.set('');
    this.resetEntryState();
  }
  private resetEntryState(): void {
    this.pending = null;
    this.hasPendingRequest.set(false);
    this.busyOperationToken++;
    this.historyOperationToken++;
    this.busy.set(false);
    this.historyLoading.set(false);
    this.historyError.set('');
    this.historyPages.set([]);
    this.uncertain.set(false);
    this.receiptState.set('UNCONFIRMED');
    this.recoveryError.set('');
    this.error.set('');
    this.formError.set('');
    this.editorOpen.set(false);
    this.reviewing.set(false);
    this.confirmedReceipt.set(false);
    this.accessUnavailable.set(false);
    this.authorizedFallback.set(null);
    this.form = { workDate: '', startTime: '09:00', durationMinutes: 60, activity: '', narrative: '', reason: '' };
  }
  private beginEdit(row: TimeEntryChangeSource, kind: TimeEntryChangeKind): void {
    this.form = { workDate: row.workDate, startTime: this.startTime(row.startMinute), durationMinutes: row.durationMinutes,
      activity: row.activity, narrative: row.narrative, reason: '' };
    this.editorOpen.set(true);
    this.error.set('');
    // The panel's action input is sourced from the server-projected capability, never from a guessed status transition.
    if (this.action() !== kind) this.editorOpen.set(false);
  }

  formIsValid(): boolean {
    const form = this.form;
    const source = this.source();
    const startMinute = this.minuteForStart(form.startTime);
    return !!source && this.validWorkDate(form.workDate) && Number.isSafeInteger(startMinute) && startMinute >= 0 && startMinute <= 1439 &&
      Number.isSafeInteger(form.durationMinutes) && form.durationMinutes >= 1 && form.durationMinutes <= 1440 &&
      startMinute + form.durationMinutes <= 1440 && !!form.activity.trim() && form.activity.trim().length <= 20000 &&
      form.narrative.length <= 2000 && form.reason.trim().length > 0 && form.reason.trim().length <= 2000 &&
      BigInt(source.revision) < int64Maximum;
  }
  reviewChange(): void {
    if (!this.editorOpen() || !this.formIsValid()) {
      this.formError.set('Enter a valid work date, bounded UTC interval, activity and reason.');
      return;
    }
    const intent = this.captureFocusIntent();
    this.formError.set('');
    this.reviewing.set(true);
    this.focusAfterRender(() => this.reviewHeading()?.nativeElement, intent);
  }
  private validWorkDate(value: string): boolean {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
    const parsed = new Date(`${value}T00:00:00Z`);
    return Number.isFinite(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
  }
  private minuteForStart(value: string): number {
    const match = /^(?:([01][0-9]|2[0-3])):([0-5][0-9])$/.exec(value);
    return match ? Number(match[1]) * 60 + Number(match[2]) : -1;
  }
  startTime(minute: number): string { return `${String(Math.floor(minute / 60)).padStart(2, '0')}:${String(minute % 60).padStart(2, '0')}`; }
  utcTime(value: string): string {
    const millis = Date.parse(value);
    if (!Number.isFinite(millis)) return 'Invalid retained time';
    const iso = new Date(millis).toISOString();
    const fraction = /\.(\d+)(?=Z|[+-]\d{2}:\d{2}$)/.exec(value)?.[1];
    return `${iso.slice(0, 19)}${fraction ? `.${fraction.padEnd(7, '0')}` : ''} UTC`;
  }

  async saveReviewedChange(): Promise<void> {
    if (!this.reviewing() || !this.formIsValid() || this.busy() || this.uncertain()) return;
    const row = this.source();
    if (!row) return;
    const focusIntent = this.captureFocusIntent();
    const kind = this.action();
    if (kind === 'HISTORY' || kind === 'EDIT_UNAPPROVED' && !row.canEdit || kind === 'CORRECT_APPROVED' && !row.canCorrect) return;
    let requestId: string;
    try { requestId = globalThis.crypto.randomUUID(); }
    catch {
      this.formError.set('This browser could not create a stable request identity. No time change was sent.');
      return;
    }
    const request: TimeEntryChangeRequest = Object.freeze({
      requestId, expectedRevision: row.revision, kind,
      workDate: this.form.workDate,
      startMinute: this.minuteForStart(this.form.startTime),
      durationMinutes: this.form.durationMinutes,
      activity: this.form.activity.trim(), narrative: this.form.narrative.trim(), reason: this.form.reason.trim(),
    });
    const stored: StoredPendingChange = { contextKey: this.contextKey, timeEntryId: row.id, request,
      source: { revision: row.revision, status: row.status, values: sourceValuesFor(row) } };
    if (!this.storePending(stored)) {
      this.formError.set('This tab could not retain the exact change request. Enable session storage; no request was sent.');
      return;
    }
    this.pending = stored;
    this.hasPendingRequest.set(true);
    this.reviewing.set(false);
    await this.transmitPending(focusIntent);
  }

  private async transmitPending(focusIntent: TimeChangeFocusIntent | null = null): Promise<void> {
    const pending = this.pending;
    const context = this.contextKey;
    if (!pending || !context || this.busy()) return;
    const generation = this.session.invalidation();
    const actorScope = this.actorScope;
    const operationToken = ++this.busyOperationToken;
    this.busy.set(true);
    this.error.set('');
    try {
      const result = await this.api.command<unknown>(`/api/ui/practice/time/entries/${pending.timeEntryId}/changes`, pending.request);
      if (!this.isCurrentOperation(context, generation, actorScope)) return;
      if (result.ok) {
        let resultTimeEntryId: string;
        try { resultTimeEntryId = decode(guid, result.value); }
        catch {
          this.markUncertain('The server responded, but its saved result could not be decoded. Check its receipt before retrying.', focusIntent);
          return;
        }
        const expectedSameEntry = pending.request.kind === 'EDIT_UNAPPROVED';
        if (expectedSameEntry !== (resultTimeEntryId.toLowerCase() === pending.timeEntryId.toLowerCase())) {
          this.markUncertain('The server result does not match the requested change kind. Check the saved receipt.', focusIntent);
          return;
        }
        this.clearStoredPending(context);
        this.pending = null;
        this.hasPendingRequest.set(false);
        this.uncertain.set(false);
        this.confirmedReceipt.set(true);
        this.editorOpen.set(false);
        this.completed.emit({ kind: pending.request.kind, sourceTimeEntryId: pending.timeEntryId,
          sourceStatus: pending.source.status, resultTimeEntryId, focusEntries: this.focusIntentIsCurrent(focusIntent) });
        return;
      }
      if (result.unknown || result.code.includes('idempotency') || result.code === 'request.identity-conflict') {
        this.markUncertain(result.message, focusIntent);
        return;
      }
      if (result.status === 401 || result.status === 403 || result.status === 404 || result.code === 'generation.stale') {
        this.clearPrivateForScopeLoss();
        return;
      }
      this.clearStoredPending(context);
      this.pending = null;
      this.hasPendingRequest.set(false);
      this.uncertain.set(false);
      this.receiptState.set('UNCONFIRMED');
      this.error.set(result.message);
      this.reviewing.set(false);
      this.editorOpen.set(true);
    } catch {
      if (this.isCurrentOperation(context, generation, actorScope))
        this.markUncertain('The request outcome could not be confirmed. Check its saved receipt before retrying.', focusIntent);
    } finally {
      if (operationToken === this.busyOperationToken) this.busy.set(false);
    }
  }

  private markUncertain(message: string, focusIntent: TimeChangeFocusIntent | null = null): void {
    this.uncertain.set(true);
    this.receiptState.set('UNCONFIRMED');
    this.recoveryMessage.set(message || 'The request outcome could not be confirmed. Check its saved receipt before retrying.');
    this.focusAfterRender(() => this.recoveryHeading()?.nativeElement, focusIntent);
  }

  async checkSavedChange(automatic = false): Promise<void> {
    const pending = this.pending;
    const context = this.contextKey;
    const session = this.session.current();
    if (!this.uncertain() || !pending || !context || !session || this.busy()) return;
    const focusIntent = automatic ? null : this.captureFocusIntent();
    const operationToken = ++this.busyOperationToken;
    this.busy.set(true);
    this.recoveryError.set('');
    this.receiptState.set('UNCONFIRMED');
    const url = `/api/ui/practice/time/entries/${pending.timeEntryId}/changes/requests/${pending.request.requestId}`;
    try {
      const lookup = await this.readScoped(url, lookupDecoder, context);
      if (!lookup) {
        this.focusAfterRender(() => this.recoveryHeading()?.nativeElement, focusIntent);
        return;
      }
      if (!lookup.found) {
        this.receiptState.set('NOT_FOUND');
        this.recoveryMessage.set('No saved receipt was visible at this check. You may check again or explicitly retry the exact saved request with its original identity.');
        if (!this.source()) {
          this.authorizedFallback.set(this.fallbackFromPending(pending, session));
          void this.loadHistoryPage(null, true, context);
        }
        this.focusAfterRender(() => this.recoveryHeading()?.nativeElement, focusIntent);
        return;
      }
      const receipt = lookup.receipt!;
      if (!this.receiptMatchesPending(receipt, pending, session)) {
        this.receiptState.set('MISMATCH');
        this.recoveryError.set('The retained receipt does not match this exact request. Keep it unresolved and do not submit another change.');
        this.focusAfterRender(() => this.recoveryHeading()?.nativeElement, focusIntent);
        return;
      }
      this.clearStoredPending(context);
      this.pending = null;
      this.hasPendingRequest.set(false);
      this.uncertain.set(false);
      this.receiptState.set('FOUND');
      this.confirmedReceipt.set(true);
      this.editorOpen.set(false);
      this.completed.emit({ kind: receipt.kind, sourceTimeEntryId: receipt.timeEntryId,
        sourceStatus: receipt.sourceStatusBefore, resultTimeEntryId: receipt.resultTimeEntryId,
        focusEntries: this.focusIntentIsCurrent(focusIntent) });
    } finally {
      if (operationToken === this.busyOperationToken) this.busy.set(false);
    }
  }

  async retryExactChange(): Promise<void> {
    if (!this.uncertain() || this.receiptState() !== 'NOT_FOUND' || !this.pending || this.busy()) return;
    // The retry uses the frozen stored request object; current form values are never read or resubmitted.
    await this.transmitPending(this.captureFocusIntent());
  }

  private fallbackFromPending(pending: StoredPendingChange, session: Session): TimeEntryChangeSource {
    return { id: pending.timeEntryId, userId: session.userId, workDate: pending.source.values.workDate,
      startMinute: pending.source.values.startMinute, durationMinutes: pending.source.values.durationMinutes,
      activity: pending.source.values.activity, narrative: pending.source.values.narrative, status: pending.source.status,
      revision: pending.source.revision, canEdit: false, canCorrect: false, hasChanges: false,
      supersedesId: null, correctionReason: null };
  }

  private receiptMatchesPending(receipt: TimeEntryChangeReceipt, pending: StoredPendingChange, session: Session): boolean {
    const request = pending.request;
    const expectedResultRevision = BigInt(request.expectedRevision) + 1n;
    const expectedBeforeStatus = pending.source.status;
    const edit = request.kind === 'EDIT_UNAPPROVED';
    return receipt.actorId.toLowerCase() === session.userId.toLowerCase() && receipt.actorEpoch === session.generation &&
      receipt.requestId.toLowerCase() === request.requestId.toLowerCase() && receipt.kind === request.kind &&
      receipt.timeEntryId.toLowerCase() === pending.timeEntryId.toLowerCase() &&
      receipt.sourceRevision === request.expectedRevision && BigInt(receipt.resultRevision) === expectedResultRevision &&
      receipt.reason === request.reason && equalValues(receipt.before, pending.source.values) && equalValues(receipt.after, valuesFor(request)) &&
      receipt.sourceStatusBefore === expectedBeforeStatus &&
      (edit
        ? receipt.sourceStatusAfter === expectedBeforeStatus && receipt.resultStatus === expectedBeforeStatus &&
          receipt.resultTimeEntryId.toLowerCase() === pending.timeEntryId.toLowerCase()
        : expectedBeforeStatus === 'APPROVED' && receipt.sourceStatusAfter === 'SUPERSEDED' && receipt.resultStatus === 'DRAFT' &&
          receipt.resultTimeEntryId.toLowerCase() !== pending.timeEntryId.toLowerCase());
  }

  private async readScoped<T>(url: string, decoder: Decoder<T>, context: string): Promise<T | null> {
    const generation = this.session.invalidation();
    const actorScope = this.actorScope;
    try {
      const raw = await firstValueFrom(this.http.get<unknown>(url).pipe(timeout(30000)));
      if (!this.isCurrentOperation(context, generation, actorScope)) return null;
      return decode(decoder, raw);
    } catch (error) {
      if (!this.isCurrentOperation(context, generation, actorScope)) return null;
      if (error instanceof HttpErrorResponse) {
        const code = error.error && typeof error.error.code === 'string' ? error.error.code : '';
        if (error.status === 401) void this.session.refresh();
        if (error.status === 401 || error.status === 403 || error.status === 404 || code === 'generation.stale') {
          this.clearPrivateForScopeLoss();
          return null;
        }
      }
      const message = error instanceof DecodeError
        ? 'The change-history response is unsupported. No values were inferred.'
        : 'Change history or receipt lookup is temporarily unavailable. Keep any unresolved request unchanged and retry the lookup.';
      if (url.includes('/requests/')) this.recoveryError.set(message);
      else this.historyError.set(message);
      return null;
    }
  }

  private isCurrentOperation(context: string, generation: number, actorScope: string): boolean {
    return !this.destroyed && generation === this.session.invalidation() && actorScope === actorScopeFor(this.session.current()) &&
      context === this.contextKey && context === `${actorScope}:${this.requestedEntryId().toLowerCase()}`;
  }

  private async loadHistoryPage(cursor: string | null, replace: boolean, context: string): Promise<void> {
    if (!context || this.historyLoading()) return;
    const operationToken = ++this.historyOperationToken;
    this.historyLoading.set(true);
    this.historyError.set('');
    const query = cursor ? `?beforeReceiptId=${encodeURIComponent(cursor)}` : '';
    try {
      const entryId = this.requestedEntryId();
      const page = await this.readScoped(`/api/ui/practice/time/entries/${entryId}/changes${query}`, pageShape, context);
      if (!page) return;
      if (page.pageSize !== 20 || page.items.length > page.pageSize || page.hasMore !== !!page.nextCursor ||
          page.hasMore && (page.items.length !== page.pageSize || page.nextCursor?.toLowerCase() !== page.items.at(-1)?.id.toLowerCase()) ||
          page.items.some(item => item.timeEntryId.toLowerCase() !== entryId.toLowerCase() &&
            item.resultTimeEntryId.toLowerCase() !== entryId.toLowerCase())) {
        this.historyError.set('The retained change-history page does not match its bounded entry scope.');
        return;
      }
      if (replace) this.historyPages.set([page]);
      else {
        const previousIds = new Set(this.historyPages().flatMap(current => current.items.map(item => item.id.toLowerCase())));
        if (page.items.some(item => previousIds.has(item.id.toLowerCase()))) {
          this.historyError.set('The retained change-history cursor repeated an earlier receipt.');
          return;
        }
        this.historyPages.update(pages => [...pages, page]);
      }
    } finally {
      if (operationToken === this.historyOperationToken) this.historyLoading.set(false);
    }
  }
  loadHistoryFromStart(): void { void this.loadHistoryPage(null, true, this.contextKey); }
  nextHistoryPage(): void {
    const page = this.currentHistory();
    if (this.busy() || this.historyLoading() || !page?.hasMore || !page.nextCursor) return;
    void this.loadHistoryPage(page.nextCursor, false, this.contextKey);
  }
  previousHistoryPage(): void {
    if (this.busy() || this.historyLoading() || this.historyPages().length < 2) return;
    this.historyPages.update(pages => pages.slice(0, -1));
  }

  private restorePending(context: string, entryId: string): void {
    try {
      if (typeof sessionStorage === 'undefined') return;
      const key = pendingKey(context);
      const raw = sessionStorage.getItem(key);
      if (!raw) return;
      const stored = decode(storedPendingShape, JSON.parse(raw));
      if (stored.contextKey !== context || stored.timeEntryId.toLowerCase() !== entryId.toLowerCase() ||
          stored.request.requestId === '' || stored.request.expectedRevision !== stored.source.revision ||
          stored.request.kind === 'EDIT_UNAPPROVED' && !['DRAFT', 'RETURNED'].includes(stored.source.status) ||
          stored.request.kind === 'CORRECT_APPROVED' && stored.source.status !== 'APPROVED') {
        throw new DecodeError('Pending time-change context does not match.');
      }
      this.pending = stored;
      this.hasPendingRequest.set(true);
      this.uncertain.set(true);
      this.receiptState.set('UNCONFIRMED');
      this.recoveryMessage.set('A previous time change may have been saved. Check its actor-owned receipt before taking another action.');
    } catch {
      this.pending = null;
      this.uncertain.set(true);
      this.receiptState.set('MISMATCH');
      this.recoveryError.set('Saved change-recovery data could not be validated. Do not submit another change from this page.');
    }
  }

  private storePending(stored: StoredPendingChange): boolean {
    try {
      if (typeof sessionStorage === 'undefined') return false;
      sessionStorage.setItem(pendingKey(stored.contextKey), JSON.stringify(stored)); return true;
    }
    catch { return false; }
  }
  private clearStoredPending(context: string): void {
    try {
      if (typeof sessionStorage === 'undefined') return;
      sessionStorage.removeItem(pendingKey(context));
    } catch { /* Keep the in-memory request safe if storage is unavailable. */ }
  }
  private clearStoredActorScope(scope: string): void {
    try {
      if (typeof sessionStorage === 'undefined') return;
      const prefix = `${pendingPrefix}${scope}:`;
      for (let index = sessionStorage.length - 1; index >= 0; index--) {
        const key = sessionStorage.key(index);
        if (key?.startsWith(prefix)) sessionStorage.removeItem(key);
      }
    } catch { /* Storage may be unavailable. */ }
  }
  private clearOtherStoredScopes(scope: string): void {
    try {
      if (typeof sessionStorage === 'undefined' || !scope) return;
      const allowedPrefix = `${pendingPrefix}${scope}:`;
      for (let index = sessionStorage.length - 1; index >= 0; index--) {
        const key = sessionStorage.key(index);
        if (key?.startsWith(pendingPrefix) && !key.startsWith(allowedPrefix)) sessionStorage.removeItem(key);
      }
    } catch { /* Storage may be unavailable. */ }
  }
  private clearPrivateForScopeLoss(): void {
    if (this.contextKey) this.clearStoredPending(this.contextKey);
    this.contextKey = '';
    this.pending = null;
    this.hasPendingRequest.set(false);
    this.uncertain.set(false);
    this.historyPages.set([]);
    this.historyLoading.set(false);
    this.historyError.set('');
    this.error.set('');
    this.formError.set('');
    this.confirmedReceipt.set(false);
    this.editorOpen.set(false);
    this.reviewing.set(false);
    this.form = { workDate: '', startTime: '09:00', durationMinutes: 60, activity: '', narrative: '', reason: '' };
    this.authorizedFallback.set(null);
    this.privateUnavailable.set(true);
    this.accessUnavailable.set(true);
    this.recoveryMessage.set('');
    this.recoveryError.set('');
  }
}
