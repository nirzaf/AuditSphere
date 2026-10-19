import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { afterNextRender, Component, DestroyRef, effect, ElementRef, inject, Injector, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { Api, queryParam } from '../../core/api';
import { Decoder, arr, bool, guid, instant, int, nullable, obj, oneOf, sha256, str } from '../../core/decode';
import { SessionService } from '../../core/session';
import { NavigationProtected } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import { firstValueFrom, timeout } from 'rxjs';

const SOURCE_KINDS = ['TASK_DUE', 'PBC_DUE'] as const;
const RECIPIENT_KINDS = ['RESPONSIBLE_STAFF', 'ENGAGEMENT_REVIEWER'] as const;
const RULE_STATUSES = ['DRAFT', 'REVIEWED', 'PUBLISHED', 'SUPERSEDED'] as const;
const PAGE_SIZE = 25;
const dateOnly: Decoder<string> = (value, path) => {
  if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)) return value;
  throw new Error(`${path}: expected date`);
};

type RuleDefinition = {
  name: string; sourceKind: typeof SOURCE_KINDS[number]; recipientKind: typeof RECIPIENT_KINDS[number];
  offsetDays: number; repeatEveryDays: number; timeZoneId: string; quietHoursStart: string;
  quietHoursEnd: string; effectiveFromUtc: string;
};

const definitionDecoder = obj({
  name: str(160), sourceKind: oneOf(...SOURCE_KINDS), recipientKind: oneOf(...RECIPIENT_KINDS),
  offsetDays: int, repeatEveryDays: int, timeZoneId: str(100), quietHoursStart: str(5),
  quietHoursEnd: str(5), effectiveFromUtc: instant,
});

function strictNullable<T>(decoder: Decoder<T>, keys: string[]): Decoder<T> {
  return (value, path) => {
    const decoded = decoder(value, path);
    if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error(`${path}: expected object`);
    const source = value as Record<string, unknown>;
    for (const key of keys) {
      if (!Object.prototype.hasOwnProperty.call(source, key) || source[key] === undefined)
        throw new Error(`${path}.${key}: required nullable field`);
    }
    return decoded;
  };
}

function optionalNullable<T>(decoder: Decoder<T>): Decoder<T | null> {
  return (value, path) => value === undefined ? null : nullable(decoder)(value, path);
}

const catalogItemShape = obj({
  ruleId: guid, revisionId: nullable(guid), version: nullable(int), name: nullable(str(160)),
  status: nullable(oneOf(...RULE_STATUSES)), suspended: bool, revision: int, createdAt: instant,
  lastEvaluatedAt: optionalNullable(instant), lastEvaluationCode: optionalNullable(str(100)),
});
const catalogItem = strictNullable(catalogItemShape, ['revisionId', 'version', 'name', 'status']);
const cursor = strictNullable(obj({ createdAt: instant, ruleId: guid }), []);
export const decodeReminderRuleCatalog = strictNullable(obj({
  executionEnabled: bool, canManage: bool, items: arr(catalogItem, 100), hasMore: bool, nextCursor: nullable(cursor),
}), ['nextCursor']);

const revisionShape = obj({
  ruleId: guid, revisionId: guid, version: int, definition: definitionDecoder,
  status: oneOf(...RULE_STATUSES), contentDigest: sha256, revision: int, ruleRevision: int,
  createdByUserId: guid, lastEditedByUserId: guid, reviewedByUserId: nullable(guid),
  publishedByUserId: nullable(guid), createdAt: instant, updatedAt: instant,
  reviewedAt: nullable(instant), publishedAt: nullable(instant), suspended: bool,
  canEdit: bool, canReview: bool, canPublish: bool, canSuspend: bool, canResume: bool, blocker: nullable(str(1000)),
});
export const decodeReminderRuleRevision = strictNullable(revisionShape,
  ['reviewedByUserId', 'publishedByUserId', 'reviewedAt', 'publishedAt', 'blocker']);

const decisionShape = obj({
  id: guid, ruleRevisionId: guid, kind: str(40), contentDigest: sha256, rationale: str(1000),
  actorUserId: guid, occurredAt: instant,
});
const historyShape = obj({ items: arr(decisionShape, 100), hasMore: bool, nextAfterId: nullable(guid) });
export const decodeReminderRuleHistory = strictNullable(historyShape, ['nextAfterId']);

const executionItemShape = obj({
  operationId: guid, ruleRevisionId: guid, ruleVersion: int, clientId: guid, engagementId: guid,
  state: str(40), errorCode: nullable(str(100)), createdAt: instant, nextAttemptAt: instant,
  sourceKind: nullable(str(40)), sourceId: nullable(guid), dueRevision: nullable(int),
  sourceDueDate: nullable(dateOnly),
  recipientUserId: nullable(guid), sourceHref: nullable(str(1000)), createdTaskId: nullable(guid),
});
const executionItem = strictNullable(executionItemShape, ['errorCode', 'sourceKind', 'sourceId', 'dueRevision', 'sourceDueDate', 'recipientUserId', 'sourceHref', 'createdTaskId']);
const executionCursorShape = obj({ createdAt: instant, operationId: guid });
const executionCursor = strictNullable(executionCursorShape, []);
export const decodeReminderRuleExecutions = strictNullable(obj({
  items: arr(executionItem, 100), hasMore: bool, nextCursor: nullable(executionCursor), canOpenOperations: bool,
}), ['nextCursor']);

type Catalog = ReturnType<typeof decodeReminderRuleCatalog>;
type CatalogItem = Catalog['items'][number];
type RuleRevision = ReturnType<typeof decodeReminderRuleRevision>;
type Decision = ReturnType<typeof decodeReminderRuleHistory>['items'][number];
type Execution = ReturnType<typeof decodeReminderRuleExecutions>['items'][number];
type Draft = {
  ruleId: string | null; revisionId: string | null; expectedRevision: number | null; requestKey: string | null; definition: {
    name: string; sourceKind: string; recipientKind: string; offsetDays: number | null;
    repeatEveryDays: number | null; timeZoneId: string; quietHoursStart: string;
    quietHoursEnd: string; effectiveFromUtc: string;
  };
};
type SaveDraftBody = Readonly<{
  ruleId: string | null; definition: RuleDefinition; expectedRevision: number | null; requestKey?: string;
}>;
type PendingCommand =
  | { kind: 'save'; ruleId: string | null; revisionId: string | null; definition: RuleDefinition; actorId: string; owner: string;
      requestKey: string | null; body: SaveDraftBody }
  | { kind: 'review' | 'publish' | 'suspend' | 'resume'; ruleId: string; revisionId: string; digest: string; rationale: string; actorId: string; owner: string };

function blankDraft(ruleId: string | null = null, requestKey: string | null = null): Draft {
  return { ruleId, revisionId: null, expectedRevision: null, requestKey, definition: {
    name: '', sourceKind: '', recipientKind: '', offsetDays: null, repeatEveryDays: null,
    timeZoneId: '', quietHoursStart: '', quietHoursEnd: '', effectiveFromUtc: '',
  } };
}

function createRequestKey(): string | null {
  try { return globalThis.crypto?.randomUUID?.().toLowerCase() ?? null; }
  catch { return null; }
}

function sameDefinition(a: RuleDefinition, b: RuleDefinition): boolean {
  return a.name === b.name && a.sourceKind === b.sourceKind && a.recipientKind === b.recipientKind &&
    a.offsetDays === b.offsetDays && a.repeatEveryDays === b.repeatEveryDays && a.timeZoneId === b.timeZoneId &&
    a.quietHoursStart === b.quietHoursStart && a.quietHoursEnd === b.quietHoursEnd && a.effectiveFromUtc === b.effectiveFromUtc;
}

function validOffsetInstant(value: string): boolean {
  return /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:\d{2})$/.test(value) && Number.isFinite(Date.parse(value));
}

@Component({
  selector: 'audit-workflow-reminder-rules',
  imports: [FormsModule, MatButtonModule, RouterLink, ...SHARED],
  host: {
    '(window:beforeunload)': 'beforeUnload($event)',
    '(document:pointerdown)': 'recordFocusInteraction()',
    '(document:keydown)': 'recordFocusInteraction()',
  },
  template: `
    <audit-page-header title="Reminder rules" eyebrow="Practice management"
      description="Configure versioned internal reminders. These rules create operational follow-up only; they do not approve work, send external messages, or satisfy professional gates." />
    <p class="muted">Every source, recipient, timing and quiet-hours value is entered explicitly. A different authorized person must review the exact saved revision before publication.</p>
    <nav class="actions" aria-label="Practice management">
      <a matButton routerLink="/app/practice/workflow-templates">Workflow templates</a>
      <a matButton routerLink="/app/work">My work</a>
      <button matButton (click)="refresh()" [disabled]="busy()">Refresh</button>
      <button matButton="filled" (click)="startNewRule()" [disabled]="busy() || uncertain() || !catalog()?.canManage">New rule draft</button>
    </nav>
    @if (catalog()?.executionEnabled === false) {
      <p class="notice" role="status">Reminder execution is disabled by server configuration. Rules can be reviewed and published, but no reminder work will be queued.</p>
    }
    <audit-state [loading]="catalogLoading()" [error]="catalogError()" label="reminder rule catalog" />
    @if (catalog(); as page) {
      @if (!page.canManage) { <p class="muted">Your current scope is read-only for reminder rules.</p> }
      <div class="workspace">
        <section class="panel catalog" aria-labelledby="rules-heading">
          <h2 id="rules-heading">Firm reminder rules</h2>
          <ul class="rule-list">
            @for (item of items(); track item.ruleId) {
              <li><button matButton (click)="selectRule(item)" [attr.aria-current]="selectedRuleId() === item.ruleId ? 'true' : null">
                <strong>{{ item.name ?? 'Rule without a saved revision' }}</strong>
                <small>{{ item.status ?? 'No revision' }}@if (item.version !== null) { · v{{ item.version }} } · revision {{ item.revision }}</small>
                @if (item.suspended) { <small>Suspended</small> }
                <small>Last batch evaluated: {{ item.lastEvaluatedAt ?? 'Not recorded' }} · {{ evaluationCodeLabel(item.lastEvaluationCode) }}</small>
              </button></li>
            } @empty { <li>No reminder rules are recorded on this page.</li> }
          </ul>
          @if (hasMoreRules()) { <button matButton (click)="loadMoreRules()" [disabled]="busy() || catalogLoading()">Load more rules</button> }
        </section>

        <section class="panel detail" aria-labelledby="detail-heading">
          @if (draftMode()) {
            <h2 id="detail-heading" tabindex="-1">{{ draftMode() === 'edit' ? 'Edit reminder rule draft' : draftMode() === 'revision' ? 'New blank rule revision' : 'Create reminder rule draft' }}</h2>
            <p class="muted">Saving records a draft only. Review and publication remain separate human decisions.</p>
            <form class="rule-form" (submit)="$event.preventDefault(); saveDraft()">
              <label>Rule name <input name="ruleName" [(ngModel)]="draft.definition.name" maxlength="160" required /></label>
              <label>Source kind <select name="sourceKind" [(ngModel)]="draft.definition.sourceKind" required>
                <option value="">Choose a source</option><option value="TASK_DUE">Task due</option><option value="PBC_DUE">PBC request due</option>
              </select></label>
              <label>Recipient kind <select name="recipientKind" [(ngModel)]="draft.definition.recipientKind" required>
                <option value="">Choose a recipient</option><option value="RESPONSIBLE_STAFF">Responsible staff</option><option value="ENGAGEMENT_REVIEWER">Engagement reviewer</option>
              </select></label>
              <label>Offset (days) <input name="offsetDays" type="number" [(ngModel)]="draft.definition.offsetDays" required /></label>
              <label>Repeat every (days) <input name="repeatEveryDays" type="number" [(ngModel)]="draft.definition.repeatEveryDays" required /></label>
              <label>Time zone identifier <input name="timeZoneId" [(ngModel)]="draft.definition.timeZoneId" maxlength="100" required /></label>
              <label>Quiet hours start <input name="quietStart" type="time" [(ngModel)]="draft.definition.quietHoursStart" required /></label>
              <label>Quiet hours end <input name="quietEnd" type="time" [(ngModel)]="draft.definition.quietHoursEnd" required /></label>
              <label class="wide">Effective from (ISO 8601 with explicit UTC offset)
                <input name="effectiveFromUtc" [(ngModel)]="draft.definition.effectiveFromUtc" placeholder="YYYY-MM-DDTHH:mm:ssZ" required />
                <small>Enter the instant with Z or a numeric offset; the selected time zone is a separate rule setting.</small>
              </label>
              <div class="actions wide">
                <button matButton="filled" type="submit" [disabled]="busy() || uncertain() || !catalog()?.canManage">Save exact draft</button>
                <button matButton type="button" (click)="cancelDraft($event.currentTarget)" [disabled]="busy() || uncertain()">Cancel</button>
              </div>
            </form>
          } @else if (revision(); as r) {
            <h2 id="detail-heading" tabindex="-1">{{ r.definition.name }} · v{{ r.version }}</h2>
            <p><audit-status [value]="r.status" /> · revision {{ r.revision }} · rule revision {{ r.ruleRevision }} · digest {{ r.contentDigest }}</p>
            @if (r.blocker) { <p class="notice" role="status">{{ r.blocker }}</p> }
            <dl class="facts">
              <dt>Source</dt><dd>{{ sourceLabel(r.definition.sourceKind) }}</dd>
              <dt>Recipient</dt><dd>{{ recipientLabel(r.definition.recipientKind) }}</dd>
              <dt>Offset / repeat</dt><dd>{{ r.definition.offsetDays }} days / {{ r.definition.repeatEveryDays }} days</dd>
              <dt>Time zone</dt><dd>{{ r.definition.timeZoneId }}</dd>
              <dt>Quiet hours</dt><dd>{{ r.definition.quietHoursStart }}–{{ r.definition.quietHoursEnd }}</dd>
              <dt>Effective from</dt><dd>{{ r.definition.effectiveFromUtc }}</dd>
              <dt>Created by</dt><dd>{{ r.createdByUserId }} · {{ r.createdAt }}</dd>
              <dt>Last edited by</dt><dd>{{ r.lastEditedByUserId }} · {{ r.updatedAt }}</dd>
              @if (r.reviewedByUserId) { <dt>Reviewed by</dt><dd>{{ r.reviewedByUserId }} · {{ r.reviewedAt }}</dd> }
              @if (r.publishedByUserId) { <dt>Published by</dt><dd>{{ r.publishedByUserId }} · {{ r.publishedAt }}</dd> }
              <dt>Execution</dt><dd>{{ r.suspended ? 'Suspended' : catalog()?.executionEnabled ? 'Enabled by server configuration' : 'Disabled by server configuration' }}</dd>
            </dl>
            <div class="actions">
              @if (r.canEdit) { <button matButton (click)="editDraft(r, $event.currentTarget)" [disabled]="busy() || uncertain()">Edit draft</button> }
              @if (page.canManage && (r.status === 'PUBLISHED' || r.status === 'SUPERSEDED')) {
                <button matButton (click)="startBlankRevision(r, $event.currentTarget)" [disabled]="busy() || uncertain()">New blank revision</button>
              }
            </div>
            @if (r.canReview || r.canPublish || r.canSuspend || r.canResume) {
              <section class="decision" aria-labelledby="decision-heading">
                <h3 id="decision-heading">Review and rule controls</h3>
                <label>Decision rationale <textarea name="decisionRationale" [(ngModel)]="rationale" maxlength="1000" rows="3" required></textarea></label>
                <label class="assent"><input type="checkbox" [(ngModel)]="assent" name="ruleAssent" /> I reviewed the named rule revision and its digest above.</label>
                <div class="actions">
                  @if (r.canReview) { <button matButton (click)="review(r)" [disabled]="busy() || uncertain() || !assent || !rationale.trim()">Review this revision</button> }
                  @if (r.canPublish) { <button matButton="filled" (click)="publish(r)" [disabled]="busy() || uncertain() || !assent || !rationale.trim()">Publish reviewed revision</button> }
                  @if (r.canSuspend) { <button matButton (click)="changeSuspension(r, true)" [disabled]="busy() || uncertain() || !assent || !rationale.trim()">Suspend rule</button> }
                  @if (r.canResume) { <button matButton (click)="changeSuspension(r, false)" [disabled]="busy() || uncertain() || !assent || !rationale.trim()">Resume rule</button> }
                </div>
              </section>
            }
            <section class="evidence" aria-labelledby="history-heading">
              <h3 id="history-heading">Rule history</h3>
              @if (historyError()) { <p role="alert">{{ historyError() }}</p> }
              @if (historyLoading()) { <p role="status">Loading rule history…</p> }
              <ol>@for (entry of history(); track entry.id) {
                <li><strong>{{ entry.kind }}</strong> · {{ entry.occurredAt }} · {{ entry.actorUserId }}
                  <p>{{ entry.rationale }}</p><small>Revision {{ entry.ruleRevisionId }} · {{ entry.contentDigest }}</small>
                </li>
              } @empty { @if (!historyLoading() && !historyError()) { <li>No rule history is available.</li> } }</ol>
              @if (historyHasMore()) { <button matButton (click)="loadMoreHistory()" [disabled]="busy() || historyLoading()">Load more history</button> }
            </section>
            <section class="evidence" aria-labelledby="executions-heading">
              <h3 id="executions-heading">Reminder executions</h3>
              @if (executionError()) { <p role="alert">{{ executionError() }}</p> }
              @if (executionLoading()) { <p role="status">Loading executions…</p> }
              <ul>@for (entry of executions(); track entry.operationId) {
                <li><strong>{{ entry.state }}</strong> · operation {{ entry.operationId }} · {{ entry.createdAt }}
                  <p>Rule v{{ entry.ruleVersion }} · client {{ entry.clientId }} · engagement {{ entry.engagementId }}</p>
                  @if (entry.errorCode) { <p>Error: {{ entry.errorCode }}</p> }
                  @if (entry.sourceKind || entry.sourceId) { <p>Source: {{ entry.sourceKind ?? 'Unknown source' }} · {{ entry.sourceId ?? 'No source ID' }}</p> }
                  @if (entry.sourceDueDate) { <p>Source due date {{ entry.sourceDueDate }} · due revision {{ entry.dueRevision ?? 'Not recorded' }}</p> }
                  @if (entry.recipientUserId) { <p>Recipient {{ entry.recipientUserId }}</p> }
                  @if (entry.createdTaskId) { <p>Created operational task {{ entry.createdTaskId }}</p> }
                  <p>Next attempt {{ entry.nextAttemptAt }}</p>
                </li>
              } @empty { @if (!executionLoading() && !executionError()) { <li>No reminder executions are recorded.</li> } }</ul>
              @if (executionCanOpenOperations()) { <p class="muted">Your current scope can open the related operations records from Operations.</p> }
              @if (executionsHasMore()) { <button matButton (click)="loadMoreExecutions()" [disabled]="busy() || executionLoading()">Load more executions</button> }
            </section>
          } @else if (revisionLoading()) { <p role="status">Loading selected rule revision…</p>
          } @else if (revisionError()) { <p role="alert">{{ revisionError() }}</p>
          } @else { <h2 id="detail-heading" tabindex="-1">Select a rule or create a draft</h2><p>Rule configuration, review history and execution evidence are shown only after loading an exact catalog revision.</p> }
        </section>
      </div>
    }
    @if (uncertain()) {
      <section class="notice" aria-labelledby="uncertain-heading">
        <h2 id="uncertain-heading">Command outcome needs readback</h2>
        <p>{{ message() }}</p>
        @if (hasExactDraftReceipt()) {
          <button matButton (click)="reconcile()" [disabled]="busy()">Check exact draft receipt</button>
          @if (canRetryExactDraftSave()) { <button matButton="filled" (click)="retryExactDraftSave()" [disabled]="busy()">Retry exact draft request</button> }
        } @else { <button matButton (click)="reconcile()" [disabled]="busy()">Read current saved state</button> }
      </section>
    }
    <audit-command-message [message]="message()" [failed]="failed()" />
  `,
  styles: `
    :host { display: block; }
    .workspace { display: grid; grid-template-columns: minmax(16rem, 1fr) minmax(0, 2fr); gap: 1rem; align-items: start; }
    .panel { min-width: 0; padding: 1rem; border: 1px solid var(--line, #d7dfeb); border-radius: .5rem; background: var(--surface, #fff); }
    .rule-list { display: grid; gap: .5rem; padding: 0; list-style: none; }
    .rule-list button { width: 100%; display: grid; justify-content: start; text-align: start; white-space: normal; }
    .rule-list button[aria-current='true'] { border-inline-start: .25rem solid var(--accent, #1769aa); }
    .rule-form { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: .8rem; }
    .rule-form label, .decision label { display: grid; gap: .25rem; min-width: 0; }
    .rule-form input, .rule-form select, .decision textarea { min-width: 0; max-width: 100%; }
    .wide { grid-column: 1 / -1; }
    .actions { display: flex; flex-wrap: wrap; gap: .5rem; align-items: center; margin-block: .75rem; }
    .muted { color: var(--muted, #52627a); }
    .notice { padding: .8rem; border-inline-start: .25rem solid var(--accent, #1769aa); background: var(--surface-subtle, #f2f6fa); overflow-wrap: anywhere; }
    .facts { display: grid; grid-template-columns: minmax(8rem, 1fr) minmax(0, 2fr); gap: .35rem .75rem; }
    .facts dt { font-weight: 600; } .facts dd { min-width: 0; margin: 0; overflow-wrap: anywhere; }
    .decision, .evidence { margin-block: 1.25rem; padding-block-start: .5rem; border-block-start: 1px solid var(--line, #d7dfeb); }
    .decision textarea { width: 100%; box-sizing: border-box; }
    .assent { display: flex !important; align-items: flex-start; margin-block: .75rem; }
    .evidence li { margin-block: .75rem; overflow-wrap: anywhere; }
    @media (max-width: 760px) { .workspace, .rule-form, .facts { grid-template-columns: minmax(0, 1fr); } .wide { grid-column: auto; } }
  `,
})
export class WorkflowReminderRules implements NavigationProtected {
  private readonly api = inject(Api);
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);
  private readonly hostElement = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly renderInjector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly requestedRuleId = queryParam('ruleId');
  private readonly requestedRevisionId = queryParam('revisionId');
  readonly catalog = signal<Catalog | null>(null);
  readonly items = signal<CatalogItem[]>([]);
  readonly selectedRuleId = signal<string | null>(null);
  readonly revision = signal<RuleRevision | null>(null);
  readonly draftMode = signal<'new' | 'revision' | 'edit' | null>(null);
  draft: Draft = blankDraft();
  rationale = '';
  assent = false;
  readonly hasMoreRules = signal(false);
  readonly nextRuleCursor = signal<Catalog['nextCursor']>(null);
  readonly history = signal<Decision[]>([]);
  readonly historyHasMore = signal(false);
  readonly historyNextId = signal<string | null>(null);
  readonly executions = signal<Execution[]>([]);
  readonly executionsHasMore = signal(false);
  readonly executionCursor = signal<ReturnType<typeof decodeReminderRuleExecutions>['nextCursor']>(null);
  readonly executionCanOpenOperations = signal(false);
  readonly catalogLoading = signal(false);
  readonly revisionLoading = signal(false);
  readonly historyLoading = signal(false);
  readonly executionLoading = signal(false);
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly receiptMissing = signal(false);
  readonly failed = signal(false);
  readonly message = signal('');
  readonly catalogError = signal('');
  readonly revisionError = signal('');
  readonly historyError = signal('');
  readonly executionError = signal('');
  private readonly pending = signal<PendingCommand | null>(null);
  private baseline = '';
  private owner = '';
  private ownerEpoch = 0;
  private catalogEpoch = 0;
  private revisionEpoch = 0;
  private historyEpoch = 0;
  private executionEpoch = 0;
  private readonly selectedRevisionId = signal<string | null>(null);
  private handledDeepLink = '';
  private focusInteraction = 0;
  private destroyed = false;
  readonly sourceKinds = SOURCE_KINDS;
  readonly recipientKinds = RECIPIENT_KINDS;

  constructor() {
    this.destroyRef.onDestroy(() => { this.destroyed = true; });
    effect(() => {
      this.session.invalidation();
      const current = this.session.current();
      const nextOwner = current?.staff ? `${current.firmId}:${current.userId}:${current.generation}` : '';
      untracked(() => {
        if (nextOwner === this.owner) return;
        this.owner = nextOwner;
        this.ownerEpoch++;
        this.clearProtectedState();
        if (nextOwner) void this.loadCatalog(null, false);
      });
    });
    effect(() => {
      const ruleIdValue = this.requestedRuleId();
      const revisionIdValue = this.requestedRevisionId();
      const current = this.session.current();
      this.session.invalidation();
      const owner = current?.staff ? `${current.firmId}:${current.userId}:${current.generation}` : '';
      const key = `${owner}|${ruleIdValue ?? ''}|${revisionIdValue ?? ''}`;
      if (!owner || key === this.handledDeepLink) return;
      if (!ruleIdValue && !revisionIdValue) return;
      this.handledDeepLink = key;
      untracked(() => {
        try {
          if (!ruleIdValue || !revisionIdValue) throw new Error('incomplete reminder origin link');
          const ruleId = guid(ruleIdValue, 'ruleId').toLowerCase();
          const revisionId = guid(revisionIdValue, 'revisionId').toLowerCase();
          if (this.busy() || this.uncertain() || !this.confirmNavigation()) {
            this.message.set('The requested rule cannot replace a draft or unresolved command.');
            return;
          }
          this.openExactRevision(ruleId, revisionId);
        } catch {
          this.clearSelection('The reminder origin link is invalid.');
        }
      });
    });
  }

  startNewRule(): void {
    if (!this.catalog()?.canManage || this.busy() || this.uncertain() || !this.confirmNavigation()) return;
    const requestKey = createRequestKey();
    if (!requestKey) { this.fail('A secure draft request identity is unavailable. Try again in a supported browser.'); return; }
    this.selectedRuleId.set(null); this.selectedRevisionId.set(null); this.revision.set(null);
    this.draft = blankDraft(null, requestKey); this.baseline = JSON.stringify(this.draft); this.draftMode.set('new');
    this.rationale = ''; this.assent = false;
  }

  startBlankRevision(r: RuleRevision, trigger?: EventTarget | null): void {
    if (!this.catalog()?.canManage || (r.status !== 'PUBLISHED' && r.status !== 'SUPERSEDED') || this.busy() || this.uncertain() || !this.confirmNavigation()) return;
    const requestKey = createRequestKey();
    if (!requestKey) { this.fail('A secure draft request identity is unavailable. Try again in a supported browser.'); return; }
    const next = blankDraft(r.ruleId, requestKey); next.expectedRevision = r.ruleRevision;
    this.draft = next; this.baseline = JSON.stringify(next); this.draftMode.set('revision');
    this.rationale = ''; this.assent = false;
    this.focusDetailAfterChange(trigger);
  }

  editDraft(r: RuleRevision, trigger?: EventTarget | null): void {
    if (!r.canEdit || this.busy() || this.uncertain() || !this.confirmNavigation()) return;
    const next: Draft = { ruleId: r.ruleId, revisionId: r.revisionId, expectedRevision: r.ruleRevision, requestKey: null,
      definition: { ...r.definition } };
    this.draft = next; this.baseline = JSON.stringify(next); this.draftMode.set('edit');
    this.rationale = ''; this.assent = false;
    this.focusDetailAfterChange(trigger);
  }

  cancelDraft(trigger?: EventTarget | null): void {
    if (!this.confirmNavigation()) return;
    this.draftMode.set(null); this.baseline = '';
    this.focusDetailAfterChange(trigger);
  }

  selectRule(item: CatalogItem): void {
    if (this.busy() || this.uncertain() || !this.confirmNavigation()) return;
    this.selectedRuleId.set(item.ruleId); this.selectedRevisionId.set(item.revisionId);
    this.revision.set(null); this.revisionError.set(''); this.revisionLoading.set(false);
    this.draftMode.set(null); this.baseline = ''; this.rationale = ''; this.assent = false;
    this.history.set([]); this.historyError.set(''); this.historyHasMore.set(false); this.historyNextId.set(null);
    this.executions.set([]); this.executionError.set(''); this.executionsHasMore.set(false); this.executionCursor.set(null);
    if (item.revisionId) void this.loadRevision(item.ruleId, item.revisionId);
    void this.loadHistory(item.ruleId, null, false);
    void this.loadExecutions(item.ruleId, null, false);
  }

  async saveDraft(): Promise<void> {
    const current = this.draft;
    const mode = this.draftMode();
    if (!this.catalog()?.canManage || !mode || this.busy() || this.uncertain()) return;
    const value = this.toDefinition(current);
    if (!value) { this.fail('Enter every rule field explicitly, including a valid ISO 8601 effective instant with a UTC offset.'); return; }
    const requestKey = mode === 'edit' ? null : current.requestKey;
    if (mode !== 'edit' && !requestKey) { this.fail('This draft has no retained request identity. Start a new draft before saving.'); return; }
    const body: SaveDraftBody = Object.freeze({ ruleId: current.ruleId, definition: Object.freeze({ ...value }),
      expectedRevision: current.expectedRevision, ...(requestKey ? { requestKey } : {}) });
    await this.command({ kind: 'save', ruleId: current.ruleId, revisionId: current.revisionId,
      definition: value, actorId: this.session.current()?.userId ?? '', owner: this.owner,
      requestKey, body },
      '/api/ui/practice/reminder-rules/drafts', body, 'save');
  }

  hasExactDraftReceipt(): boolean {
    const pending = this.pending();
    return pending?.kind === 'save' && pending.requestKey !== null && pending.body.requestKey === pending.requestKey;
  }

  canRetryExactDraftSave(): boolean {
    const pending = this.pending();
    return this.uncertain() && this.receiptMissing() && !this.busy() && pending?.kind === 'save' &&
      pending.owner === this.owner && pending.requestKey !== null && pending.body.requestKey === pending.requestKey;
  }

  async retryExactDraftSave(): Promise<void> {
    const pending = this.pending();
    if (!this.canRetryExactDraftSave() || pending?.kind !== 'save') return;
    this.receiptMissing.set(false);
    await this.command(pending, '/api/ui/practice/reminder-rules/drafts', pending.body, 'save', true);
  }

  review(r: RuleRevision): void {
    if (!this.isCurrentRevision(r) || !r.canReview || this.busy() || this.uncertain() || !this.assent || !this.rationale.trim()) return;
    const body = { expectedRevision: r.revision, contentDigest: r.contentDigest, rationale: this.rationale };
    void this.command({ kind: 'review', ruleId: r.ruleId, revisionId: r.revisionId,
      digest: r.contentDigest, rationale: body.rationale, actorId: this.session.current()?.userId ?? '', owner: this.owner },
      `/api/ui/practice/reminder-rules/${r.ruleId}/revisions/${r.revisionId}/review`, body, 'review');
  }

  publish(r: RuleRevision): void {
    if (!this.isCurrentRevision(r) || !r.canPublish || this.busy() || this.uncertain() || !this.assent || !this.rationale.trim()) return;
    const body = { expectedRevision: r.revision, contentDigest: r.contentDigest, rationale: this.rationale };
    void this.command({ kind: 'publish', ruleId: r.ruleId, revisionId: r.revisionId,
      digest: r.contentDigest, rationale: body.rationale, actorId: this.session.current()?.userId ?? '', owner: this.owner },
      `/api/ui/practice/reminder-rules/${r.ruleId}/revisions/${r.revisionId}/publish`, body, 'publish');
  }

  changeSuspension(r: RuleRevision, suspend: boolean): void {
    const allowed = suspend ? r.canSuspend : r.canResume;
    if (!this.isCurrentRevision(r) || !allowed || this.busy() || this.uncertain() || !this.assent || !this.rationale.trim()) return;
    const kind = suspend ? 'suspend' : 'resume';
    const body = { expectedRevision: r.ruleRevision, publishedRevisionId: r.revisionId,
      contentDigest: r.contentDigest, rationale: this.rationale };
    void this.command({ kind, ruleId: r.ruleId, revisionId: r.revisionId,
      digest: r.contentDigest, rationale: body.rationale, actorId: this.session.current()?.userId ?? '', owner: this.owner },
      `/api/ui/practice/reminder-rules/${r.ruleId}/${kind}`, body, kind);
  }

  async reconcile(): Promise<void> {
    const pending = this.pending();
    if (!pending || this.busy() || pending.owner !== this.owner) return;
    const epoch = this.ownerEpoch, generation = this.session.invalidation();
    const active = this.hostElement.nativeElement.ownerDocument.activeElement;
    const interaction = this.focusInteraction;
    this.busy.set(true); this.failed.set(false);
    try {
      let view: RuleRevision | null = null;
      if (pending.kind === 'save') {
        if (pending.requestKey && pending.body.requestKey === pending.requestKey) {
          view = await this.readCreationReceipt(pending.requestKey, epoch, generation);
          if (!this.isCurrent(epoch, generation) || pending.owner !== this.owner) return;
          if (!view) {
            this.receiptMissing.set(true);
            this.message.set('No receipt is recorded yet. The original request may still commit; retry only with the retained exact request.');
            return;
          }
          this.receiptMissing.set(false);
        } else if (pending.ruleId) {
          const page = await this.api.get(this.catalogUrl(null), decodeReminderRuleCatalog);
          if (!this.isCurrent(epoch, generation) || pending.owner !== this.owner) return;
          const item = page.items.find(x => x.ruleId === pending.ruleId);
          if (item?.revisionId) view = await this.api.get(this.revisionUrl(pending.ruleId, item.revisionId), decodeReminderRuleRevision);
          else { this.message.set('The existing rule revision is not visible yet. Read the catalog again; do not submit another draft.'); return; }
        } else {
          this.message.set('This draft has no retained request identity. Keep the command unresolved; it was not resent.');
          return;
        }
        if (!this.matchesSavedDraft(view, pending)) { this.message.set('No exact saved draft matches the submitted definition yet. Keep this command unresolved and read back again.'); return; }
      } else {
        const [latest, history] = await Promise.all([
          this.api.get(this.revisionUrl(pending.ruleId, pending.revisionId), decodeReminderRuleRevision),
          this.api.get(this.historyUrl(pending.ruleId, null, 100), decodeReminderRuleHistory),
        ]);
        if (!this.isCurrent(epoch, generation) || pending.owner !== this.owner) return;
        const expectedKind = pending.kind === 'review' ? 'REVIEWED' : pending.kind === 'publish' ? 'PUBLISHED' : pending.kind === 'suspend' ? 'SUSPENDED' : 'RESUMED';
        const event = history.items.find(x => x.ruleRevisionId === pending.revisionId && x.kind === expectedKind &&
          x.contentDigest === pending.digest && x.rationale === pending.rationale && x.actorUserId === pending.actorId);
        const stateMatches = pending.kind === 'review' ? latest.status === 'REVIEWED' && latest.reviewedByUserId === pending.actorId
          : pending.kind === 'publish' ? latest.status === 'PUBLISHED' && latest.publishedByUserId === pending.actorId
          : pending.kind === 'suspend' ? latest.suspended : !latest.suspended;
        if (!event || !stateMatches || latest.contentDigest !== pending.digest) {
          this.message.set('No exact decision record confirms this command yet. Keep it unresolved and read back again; it was not resent.'); return;
        }
        view = latest;
      }
      if (!this.isCurrent(epoch, generation) || !view) return;
      this.accept(view, 'Saved state confirmed by exact revision and audit history.');
      this.scheduleDetailFocus(active, interaction, pending.owner, epoch, generation);
    } catch (error) {
      if (this.isCurrent(epoch, generation)) {
        const message = error instanceof Error ? error.message : 'Readback failed. No command was resent.';
        this.receiptMissing.set(false);
        this.failed.set(true); this.message.set(message);
        this.clearProtectedState(message, true);
      }
    } finally { if (epoch === this.ownerEpoch) this.busy.set(false); }
  }

  async refresh(): Promise<void> {
    if (this.busy() || this.uncertain() || !this.confirmNavigation()) return;
    await this.loadCatalog(null, false);
    const id = this.selectedRuleId(), revisionId = this.selectedRevisionId();
    if (id && revisionId) await this.loadRevision(id, revisionId);
  }

  async loadMoreRules(): Promise<void> {
    const cursor = this.nextRuleCursor(); if (!cursor || this.busy() || this.catalogLoading()) return;
    await this.loadCatalog(cursor, true);
  }
  async loadMoreHistory(): Promise<void> {
    const id = this.selectedRuleId(), after = this.historyNextId();
    if (id && after && !this.uncertain() && !this.historyLoading()) await this.loadHistory(id, after, true);
  }
  async loadMoreExecutions(): Promise<void> {
    const id = this.selectedRuleId(), after = this.executionCursor();
    if (id && after && !this.uncertain() && !this.executionLoading()) await this.loadExecutions(id, after, true);
  }

  confirmNavigation(): boolean {
    if (this.busy() || this.uncertain()) { this.message.set('Wait for the command or reconcile its saved state before leaving.'); return false; }
    return !this.isDirty() || window.confirm('Discard unsaved reminder rule edits?');
  }

  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.busy() || this.uncertain() || this.isDirty()) { event.preventDefault(); event.returnValue = ''; }
  }

  recordFocusInteraction(): void { this.focusInteraction++; }

  sourceLabel(kind: string): string { return kind === 'TASK_DUE' ? 'Task due' : kind === 'PBC_DUE' ? 'PBC request due' : kind; }
  recipientLabel(kind: string): string { return kind === 'RESPONSIBLE_STAFF' ? 'Responsible staff' : kind === 'ENGAGEMENT_REVIEWER' ? 'Engagement reviewer' : kind; }
  evaluationCodeLabel(code: string | null): string {
    if (!code) return 'Outcome not recorded';
    return code.toLowerCase().split('_').map(part => part ? part[0].toUpperCase() + part.slice(1) : '').join(' ');
  }

  private focusDetailAfterChange(origin?: EventTarget | null): void {
    const host = this.hostElement.nativeElement;
    const document = host.ownerDocument;
    const active = document.activeElement;
    const trigger = origin instanceof HTMLElement ? origin : null;
    if (active !== document.body && active !== trigger) return;
    this.scheduleDetailFocus(active, this.focusInteraction, this.owner, this.ownerEpoch, this.session.invalidation());
  }

  private scheduleDetailFocus(active: Element | null, interaction: number, owner: string, epoch: number, generation: number): void {
    const host = this.hostElement.nativeElement;
    const document = host.ownerDocument;
    if (active !== document.body && (!active || !host.contains(active))) return;
    afterNextRender(() => {
      if (this.destroyed || this.focusInteraction !== interaction || this.owner !== owner || this.ownerEpoch !== epoch ||
          generation !== this.session.invalidation() ||
          (document.activeElement !== active && document.activeElement !== document.body)) return;
      host.querySelector<HTMLElement>('#detail-heading')?.focus();
    }, { injector: this.renderInjector });
  }

  private async command(pending: PendingCommand, url: string, body: unknown, successAction: string, allowExactRetry = false): Promise<void> {
    const exactRetry = allowExactRetry && pending.kind === 'save' && this.pending() === pending && this.uncertain() &&
      pending.requestKey !== null && pending.body.requestKey === pending.requestKey && body === pending.body;
    if (this.busy() || this.uncertain() && !exactRetry || pending.owner !== this.owner) return;
    const epoch = this.ownerEpoch, generation = this.session.invalidation();
    const active = this.hostElement.nativeElement.ownerDocument.activeElement;
    const interaction = this.focusInteraction;
    this.busy.set(true); this.failed.set(false); this.message.set('');
    try {
      const result = await this.api.command<unknown>(url, body);
      if (!this.isCurrent(epoch, generation) || pending.owner !== this.owner) return;
      if (result.ok) {
        if (pending.kind !== 'save') {
          if (result.value !== true) {
            this.pending.set(pending); this.uncertain.set(true);
            this.message.set('The command response was unsupported. Read the saved state before any further command.'); return;
          }
          this.pending.set(pending); this.uncertain.set(true);
          this.message.set('The command was accepted. Confirming its exact saved revision and audit history.');
          this.busy.set(false);
          await this.reconcile();
          return;
        }
        let view: RuleRevision;
        try { view = decodeReminderRuleRevision(result.value, 'command response'); }
        catch {
          this.pending.set(pending); this.uncertain.set(true); this.receiptMissing.set(false);
          this.message.set('The command response was unsupported. Checking the exact draft receipt before any retry.');
          this.busy.set(false); await this.reconcile(); return;
        }
        if (!this.matchesCommand(view, pending)) {
          this.pending.set(pending); this.uncertain.set(true); this.receiptMissing.set(false);
          this.message.set('The command response did not confirm the exact submitted revision. Checking the exact receipt.');
          this.busy.set(false); await this.reconcile(); return;
        }
        this.accept(view, successAction === 'save' ? 'Reminder rule draft saved.' : `Reminder rule ${successAction} recorded.`);
        this.scheduleDetailFocus(active, interaction, pending.owner, epoch, generation);
      } else if (result.unknown) {
        this.pending.set(pending); this.uncertain.set(true); this.receiptMissing.set(false);
        this.message.set('The command outcome is unconfirmed. Checking the exact saved receipt; no new request identity will be created.');
        this.busy.set(false);
        await this.reconcile();
      } else if (exactRetry) {
        this.pending.set(pending); this.uncertain.set(true); this.receiptMissing.set(false);
        this.failed.set(true); this.message.set(`${result.message} The original request remains unresolved; check its receipt before retrying again.`);
        if (result.status === 401) await this.session.refresh();
      } else {
        this.failed.set(true); this.message.set(result.message);
        if (result.status === 403 || result.status === 401) this.clearProtectedState(result.message);
        else await this.refreshAfterCommand(pending.ruleId, pending.revisionId);
      }
    } catch {
      if (this.isCurrent(epoch, generation)) {
        this.pending.set(pending); this.uncertain.set(true); this.receiptMissing.set(false);
        this.message.set('The command outcome is unconfirmed. Checking the exact saved receipt; no new request identity will be created.');
        this.busy.set(false); await this.reconcile();
      }
    } finally { if (epoch === this.ownerEpoch) this.busy.set(false); }
  }

  private async loadCatalog(cursorValue: Catalog['nextCursor'], append: boolean): Promise<void> {
    const epoch = this.ownerEpoch, generation = this.session.invalidation(), request = ++this.catalogEpoch;
    if (!append) { this.catalogError.set(''); this.catalogLoading.set(true); }
    else this.catalogLoading.set(true);
    try {
      const page = await this.api.get(this.catalogUrl(cursorValue), decodeReminderRuleCatalog);
      if (!this.isCurrent(epoch, generation) || request !== this.catalogEpoch) return;
      if (page.items.length > PAGE_SIZE || page.hasMore !== (page.nextCursor !== null)) throw new Error('Unsupported reminder catalog page.');
      const current = append ? this.items() : [];
      const existing = new Set(current.map(x => x.ruleId));
      this.items.set([...current, ...page.items.filter(x => !existing.has(x.ruleId))]);
      this.catalog.set(page); this.hasMoreRules.set(page.hasMore); this.nextRuleCursor.set(page.nextCursor);
    } catch (error) {
      if (this.isCurrent(epoch, generation) && request === this.catalogEpoch) {
        const text = error instanceof Error ? error.message : 'Reminder rules could not be loaded.';
        this.clearProtectedState(text); this.catalogError.set(text);
      }
    } finally { if (epoch === this.ownerEpoch && request === this.catalogEpoch) this.catalogLoading.set(false); }
  }

  private async loadRevision(ruleId: string, revisionId: string): Promise<void> {
    const epoch = this.ownerEpoch, generation = this.session.invalidation(), request = ++this.revisionEpoch;
    this.revisionLoading.set(true); this.revisionError.set('');
    try {
      const view = await this.api.get(this.revisionUrl(ruleId, revisionId), decodeReminderRuleRevision);
      if (!this.isCurrent(epoch, generation) || request !== this.revisionEpoch || this.selectedRuleId() !== ruleId || this.selectedRevisionId() !== revisionId) return;
      if (view.ruleId !== ruleId || view.revisionId !== revisionId) throw new Error('The server returned a different reminder rule revision.');
      this.revision.set(view);
    } catch (error) {
      if (this.isCurrent(epoch, generation) && request === this.revisionEpoch) {
        const text = error instanceof Error ? error.message : 'The selected revision is unavailable.';
        this.clearProtectedState(text); this.revisionError.set(text);
      }
    } finally { if (epoch === this.ownerEpoch && request === this.revisionEpoch) this.revisionLoading.set(false); }
  }

  private async loadHistory(ruleId: string, afterId: string | null, append: boolean): Promise<void> {
    const epoch = this.ownerEpoch, generation = this.session.invalidation(), request = ++this.historyEpoch;
    this.historyLoading.set(true); this.historyError.set('');
    try {
      const page = await this.api.get(this.historyUrl(ruleId, afterId, PAGE_SIZE), decodeReminderRuleHistory);
      if (!this.isCurrent(epoch, generation) || request !== this.historyEpoch || this.selectedRuleId() !== ruleId) return;
      if (page.items.length > PAGE_SIZE || page.hasMore !== (page.nextAfterId !== null)) throw new Error('Unsupported reminder history page.');
      const old = append ? this.history() : [], ids = new Set(old.map(x => x.id));
      this.history.set([...old, ...page.items.filter(x => !ids.has(x.id))]);
      this.historyHasMore.set(page.hasMore); this.historyNextId.set(page.nextAfterId);
    } catch (error) {
      if (this.isCurrent(epoch, generation) && request === this.historyEpoch) {
        const text = error instanceof Error ? error.message : 'Rule history is unavailable.';
        this.clearProtectedState(text); this.historyError.set(text);
      }
    } finally { if (epoch === this.ownerEpoch && request === this.historyEpoch) this.historyLoading.set(false); }
  }

  private async loadExecutions(ruleId: string, after: ReturnType<typeof decodeReminderRuleExecutions>['nextCursor'], append: boolean): Promise<void> {
    const epoch = this.ownerEpoch, generation = this.session.invalidation(), request = ++this.executionEpoch;
    this.executionLoading.set(true); this.executionError.set('');
    try {
      const page = await this.api.get(this.executionsUrl(ruleId, after, PAGE_SIZE), decodeReminderRuleExecutions);
      if (!this.isCurrent(epoch, generation) || request !== this.executionEpoch || this.selectedRuleId() !== ruleId) return;
      if (page.items.length > PAGE_SIZE || page.hasMore !== (page.nextCursor !== null)) throw new Error('Unsupported reminder execution page.');
      const old = append ? this.executions() : [], ids = new Set(old.map(x => x.operationId));
      this.executions.set([...old, ...page.items.filter(x => !ids.has(x.operationId))]);
      this.executionsHasMore.set(page.hasMore); this.executionCursor.set(page.nextCursor); this.executionCanOpenOperations.set(page.canOpenOperations);
    } catch (error) {
      if (this.isCurrent(epoch, generation) && request === this.executionEpoch) {
        const text = error instanceof Error ? error.message : 'Reminder executions are unavailable.';
        this.clearProtectedState(text); this.executionError.set(text);
      }
    } finally { if (epoch === this.ownerEpoch && request === this.executionEpoch) this.executionLoading.set(false); }
  }

  private async refreshAfterCommand(ruleId: string | null, revisionId: string | null): Promise<void> {
    await this.loadCatalog(null, false);
    if (ruleId && revisionId && this.selectedRuleId() === ruleId) await this.loadRevision(ruleId, revisionId);
  }

  private async readCreationReceipt(requestKey: string, epoch: number, generation: number): Promise<RuleRevision | null> {
    let raw: unknown;
    try {
      raw = await firstValueFrom(this.http.get<unknown>(this.receiptUrl(requestKey)).pipe(timeout(30000)));
    } catch (error) {
      const status = error instanceof HttpErrorResponse ? error.status : 0;
      if (status === 404) return null;
      if (status === 401) {
        await this.session.refresh();
        throw new Error('Your session ended while checking the draft receipt. Sign in again; the original request identity is retained.');
      }
      if (status === 403)
        throw new Error('Current access does not allow reading this draft receipt. The original request remains unresolved.');
      throw new Error('The exact draft receipt is temporarily unavailable. The original request may still commit.');
    }
    if (!this.isCurrent(epoch, generation)) return null;
    try { return decodeReminderRuleRevision(raw, 'draft receipt'); }
    catch { throw new Error('The draft receipt response was unsupported. Keep the original request unresolved.'); }
  }

  private accept(view: RuleRevision, confirmation: string): void {
    this.revision.set(view); this.selectedRuleId.set(view.ruleId); this.selectedRevisionId.set(view.revisionId);
    this.draftMode.set(null); this.baseline = ''; this.rationale = ''; this.assent = false;
    this.pending.set(null); this.uncertain.set(false); this.receiptMissing.set(false); this.failed.set(false); this.message.set(confirmation);
    void this.loadCatalog(null, false);
    void this.loadHistory(view.ruleId, null, false);
    void this.loadExecutions(view.ruleId, null, false);
  }

  private openExactRevision(ruleId: string, revisionId: string): void {
    this.selectedRuleId.set(ruleId); this.selectedRevisionId.set(revisionId);
    this.revision.set(null); this.revisionError.set(''); this.revisionLoading.set(false);
    this.draftMode.set(null); this.baseline = ''; this.rationale = ''; this.assent = false;
    this.history.set([]); this.historyError.set(''); this.historyHasMore.set(false); this.historyNextId.set(null);
    this.executions.set([]); this.executionError.set(''); this.executionsHasMore.set(false); this.executionCursor.set(null);
    void this.loadRevision(ruleId, revisionId);
    void this.loadHistory(ruleId, null, false);
    void this.loadExecutions(ruleId, null, false);
  }

  private clearSelection(message: string): void {
    this.selectedRuleId.set(null); this.selectedRevisionId.set(null); this.revision.set(null);
    this.draftMode.set(null); this.baseline = ''; this.rationale = ''; this.assent = false;
    this.history.set([]); this.historyHasMore.set(false); this.historyNextId.set(null);
    this.executions.set([]); this.executionsHasMore.set(false); this.executionCursor.set(null);
    this.revisionError.set(message);
    this.revisionEpoch++; this.historyEpoch++; this.executionEpoch++;
    this.revisionLoading.set(false); this.historyLoading.set(false); this.executionLoading.set(false);
  }

  private isCurrentRevision(r: RuleRevision): boolean {
    const current = this.revision();
    return !!current && current.ruleId === r.ruleId && current.revisionId === r.revisionId &&
      this.selectedRuleId() === r.ruleId && this.selectedRevisionId() === r.revisionId;
  }

  private matchesCommand(view: RuleRevision, pending: PendingCommand): boolean {
    if ((pending.ruleId !== null && view.ruleId !== pending.ruleId) || (pending.kind !== 'save' && view.revisionId !== pending.revisionId) ||
      (pending.kind === 'save' && pending.revisionId !== null && view.revisionId !== pending.revisionId)) return false;
    if (pending.kind === 'save') return this.matchesSavedDraft(view, pending);
    if (view.contentDigest !== pending.digest) return false;
    return pending.kind === 'review' ? view.status === 'REVIEWED' && view.reviewedByUserId === pending.actorId
      : pending.kind === 'publish' ? view.status === 'PUBLISHED' && view.publishedByUserId === pending.actorId
      : pending.kind === 'suspend' ? view.suspended : !view.suspended;
  }

  private matchesSavedDraft(view: RuleRevision, pending: Extract<PendingCommand, { kind: 'save' }>): boolean {
    return (pending.ruleId === null || view.ruleId === pending.ruleId) &&
      (pending.ruleId !== null || view.createdByUserId === pending.actorId) &&
      view.status === 'DRAFT' && view.lastEditedByUserId === pending.actorId &&
      sameDefinition(view.definition, pending.definition);
  }

  private toDefinition(draft: Draft): RuleDefinition | null {
    const d = draft.definition;
    if (!d.name || !(SOURCE_KINDS as readonly string[]).includes(d.sourceKind) || !(RECIPIENT_KINDS as readonly string[]).includes(d.recipientKind) ||
      !Number.isSafeInteger(d.offsetDays) || !Number.isSafeInteger(d.repeatEveryDays) || !d.timeZoneId ||
      !d.quietHoursStart || !d.quietHoursEnd || !validOffsetInstant(d.effectiveFromUtc)) return null;
    return { name: d.name, sourceKind: d.sourceKind as RuleDefinition['sourceKind'], recipientKind: d.recipientKind as RuleDefinition['recipientKind'],
      offsetDays: d.offsetDays!, repeatEveryDays: d.repeatEveryDays!, timeZoneId: d.timeZoneId,
      quietHoursStart: d.quietHoursStart, quietHoursEnd: d.quietHoursEnd, effectiveFromUtc: d.effectiveFromUtc };
  }

  private catalogUrl(after: Catalog['nextCursor']): string {
    const query = new URLSearchParams({ pageSize: String(PAGE_SIZE) });
    if (after) { query.set('afterCreatedAt', after.createdAt); query.set('afterRuleId', after.ruleId); }
    return `/api/ui/practice/reminder-rules?${query}`;
  }
  private revisionUrl(ruleId: string, revisionId: string): string { return `/api/ui/practice/reminder-rules/${ruleId}/revisions/${revisionId}`; }
  private historyUrl(ruleId: string, afterId: string | null, pageSize: number): string {
    const query = new URLSearchParams({ pageSize: String(pageSize) }); if (afterId) query.set('afterId', afterId);
    return `/api/ui/practice/reminder-rules/${ruleId}/history?${query}`;
  }
  private executionsUrl(ruleId: string, after: ReturnType<typeof decodeReminderRuleExecutions>['nextCursor'], pageSize: number): string {
    const query = new URLSearchParams({ pageSize: String(pageSize) });
    if (after) { query.set('afterCreatedAt', after.createdAt); query.set('afterOperationId', after.operationId); }
    return `/api/ui/practice/reminder-rules/${ruleId}/executions?${query}`;
  }
  private receiptUrl(requestKey: string): string { return `/api/ui/practice/reminder-rules/drafts/${requestKey}/receipt`; }
  private isCurrent(epoch: number, generation: number): boolean {
    return epoch === this.ownerEpoch && generation === this.session.invalidation() && !!this.session.current()?.staff;
  }
  private isDirty(): boolean { return this.draftMode() !== null && JSON.stringify(this.draft) !== this.baseline; }
  private fail(message: string): void { this.failed.set(true); this.message.set(message); }

  private clearProtectedState(message = '', preservePending = false): void {
    const pending = preservePending ? this.pending() : null;
    this.catalog.set(null); this.items.set([]); this.selectedRuleId.set(null); this.selectedRevisionId.set(null); this.revision.set(null);
    this.draftMode.set(null); this.draft = blankDraft(); this.baseline = ''; this.rationale = ''; this.assent = false;
    this.hasMoreRules.set(false); this.nextRuleCursor.set(null); this.history.set([]); this.historyHasMore.set(false); this.historyNextId.set(null);
    this.executions.set([]); this.executionsHasMore.set(false); this.executionCursor.set(null); this.executionCanOpenOperations.set(false);
    this.pending.set(pending); this.uncertain.set(pending !== null); this.receiptMissing.set(false); this.catalogError.set(''); this.revisionError.set('');
    this.historyError.set(''); this.executionError.set(''); this.failed.set(false); this.message.set(message);
    this.catalogEpoch++; this.revisionEpoch++; this.historyEpoch++; this.executionEpoch++;
    this.catalogLoading.set(false); this.revisionLoading.set(false); this.historyLoading.set(false); this.executionLoading.set(false); this.busy.set(false);
  }
}
