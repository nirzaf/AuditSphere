import { afterNextRender, Component, computed, DestroyRef, ElementRef, effect, HostListener, inject, Injector, input, output, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, date, guid, int, nullable, obj, str } from '../../core/decode';
import { SessionService } from '../../core/session';
import { WorkQueueActionRegistry, type WorkQueueDispatchTicket, type WorkQueuePendingAction,
  type WorkQueueRegistryOwner } from './work-queue-action-registry';

export interface WorkflowTaskCancellationTarget {
  taskId: string;
  clientId: string;
  clientName: string;
  engagementId: string;
  engagementServiceRoute: string;
  engagementPeriod: string;
  title: string;
  taskStatus: string;
  revision: number;
  canCancel: boolean;
  canReopen: boolean;
  canSuspend?: boolean;
  canResume?: boolean;
  requiredTask?: boolean | null;
  applicabilityCondition?: string | null;
}
export type WorkflowTaskRecoveryAction = 'cancel' | 'reopen' | 'suspend' | 'resume';
const RECOVERY_COPY: Record<WorkflowTaskRecoveryAction, { noun: string; title: string; review: string; confirm: string }> = {
  cancel: { noun: 'cancellation', title: 'Review task cancellation', review: 'Review cancellation', confirm: 'Confirm task cancellation' },
  reopen: { noun: 'reopening', title: 'Review task reopening', review: 'Review reopening', confirm: 'Confirm task reopening' },
  suspend: { noun: 'hold', title: 'Review putting the task on hold', review: 'Review hold', confirm: 'Confirm task hold' },
  resume: { noun: 'resumption', title: 'Review resuming the task', review: 'Review resumption', confirm: 'Confirm task resumption' },
};

/** A cancelled task resumes; a completed task returns to active work for correction. The server flag decides. */
export function canReopenTask(task: Pick<WorkflowTaskCancellationTarget, 'canReopen' | 'taskStatus'>): boolean {
  return task.canReopen && (task.taskStatus === 'CANCELLED' || task.taskStatus === 'COMPLETED');
}

const workflowTaskWorkspaceShape = obj({
  engagementId: guid,
  truncated: bool,
  tasks: arr(obj({
    id: guid,
    assigneeUserId: nullable(guid),
    assigneeDisplayName: nullable(str(200)),
    revision: int,
    status: str(40),
    dueDate: (value: unknown, path: string) => value === undefined ? undefined : nullable(date)(value, path),
    onHold: (value: unknown, path: string) => value === undefined ? null : nullable(bool)(value, path),
  }), 500),
});

/** Exact scoped task workspace used for post-timeout observation; it grants no mutation authority. */
export const decodeWorkflowTaskWorkspace = workflowTaskWorkspaceShape;
type Workspace = ReturnType<typeof decodeWorkflowTaskWorkspace>;
type Intent = { action: WorkflowTaskRecoveryAction; expectedRevision: number; reason: string };
type RecoveryState = 'editing' | 'review' | 'cleanup' | 'finalizing' | 'unknown' | 'retryable' | 'observed' | 'stale' | 'confirmed' | 'refused';
type FocusOrigin = { element: Element; interaction: number };

@Component({
  selector: 'audit-workflow-task-cancellation',
  imports: [MatButtonModule],
  template: `
    <section class="panel" aria-labelledby="cancellation-heading">
      <h2 #panelHeading id="cancellation-heading" tabindex="-1">{{ copy().title }}</h2>
      @if (actionMode() === 'suspend') {
        <p>Putting a task on hold pauses it. It leaves My tasks, the due-date and client-waiting views and reminders, and it cannot be completed or receive new time until a manager resumes it. Its owner, due date, time and evidence are kept.</p>
      } @else if (actionMode() === 'resume') {
        <p>Resuming returns the task to the queue views and reminders so it can be worked on and completed again.</p>
      } @else if (actionMode() === 'cancel') {
        <p>Cancellation only ends this operational task. It does not approve or complete source audit work. A cancelled required predecessor continues to block its dependent task.</p>
      } @else if (reopensCompletedWork()) {
        <p>Reopening returns this completed task to active work so it can be corrected and completed again. Its original completion stays in task activity, and tasks that depend on it wait until it is completed again. It does not approve or make a professional decision about source audit work.</p>
      } @else {
        <p>Reopening restores operational task tracking only. It does not approve, complete, or make a professional decision about source audit work.</p>
      }
      <dl>
        <dt>Task</dt><dd>{{ task().title }} · {{ task().taskId }}</dd>
        <dt>Client</dt><dd>{{ task().clientName }} · {{ task().clientId }}</dd>
        <dt>Engagement scope</dt><dd>{{ task().engagementServiceRoute }} · {{ task().engagementPeriod }} · {{ task().engagementId }}</dd>
        <dt>Current task revision</dt><dd>{{ task().revision }}</dd>
      </dl>

      @if (state() === 'editing') {
        @if (canMarkNotApplicable()) {
          <p>Applies when: {{ task().applicabilityCondition }}. Cancelling this conditional step leaves it blocked in progress until its applicability has been independently reviewed.</p>
          <label class="assent"><input type="checkbox" id="task-not-applicable" [checked]="notApplicable()"
            [disabled]="busy()" (change)="notApplicable.set($any($event.target).checked)" />
            Record why this conditional step may not apply to this engagement and reporting period.
          </label>
          <small>The reason is saved in task activity. It is not an approved applicability decision and does not approve underlying professional work.</small>
        }
        <label for="task-cancellation-reason">Reason for {{ copy().noun }}</label>
        <textarea #reasonEditor id="task-cancellation-reason" rows="4" maxlength="2000" [value]="reason"
          [disabled]="busy()" (input)="updateReason($any($event.target).value)"
          aria-describedby="task-cancellation-reason-help"></textarea>
        <small id="task-cancellation-reason-help">Enter a reason of up to 2,000 characters. It will be retained in task activity.</small>
        <button matButton="filled" type="button" (click)="prepareReview()" [disabled]="busy() || !canPerform(task(), actionMode())">
          {{ copy().review }}
        </button>
      }

      @if (intent(); as reviewed) {
        <dl class="reviewed-intent">
          <dt>Reviewed task</dt><dd>{{ task().title }} · {{ task().taskId }}</dd>
          <dt>Reviewed client</dt><dd>{{ task().clientId }}</dd>
          <dt>Reviewed engagement</dt><dd>{{ task().engagementId }}</dd>
          <dt>Expected revision</dt><dd>{{ reviewed.expectedRevision }}</dd>
          <dt>Exact reason</dt><dd class="reason">{{ reviewed.reason }}</dd>
        </dl>
      }

      @if (state() === 'review' || state() === 'retryable') {
        @if (state() === 'retryable') {
          @if (actionMode() === 'cancel') {
            <p role="status">The task is still active at the original revision. The previous request may still be in flight. A retry will send the exact original reason and revision.</p>
          } @else if (actionMode() === 'suspend' || actionMode() === 'resume') {
            <p role="status">The task is still {{ actionMode() === 'suspend' ? 'active' : 'on hold' }} at the original revision. The previous request may still be in flight. A retry will send the exact original reason and revision.</p>
          } @else {
            <p role="status">The task remains {{ reopensCompletedWork() ? 'completed' : 'cancelled' }} at the original revision. The previous request may still be in flight. A retry will send the exact original reason and revision.</p>
          }
        }
        <label class="assent"><input type="checkbox" [checked]="assent()" [disabled]="busy()"
          (change)="assent.set($any($event.target).checked)" />
          @if (state() === 'retryable') { I reviewed the unchanged task and exact original reason, and explicitly agree to retry this {{ copy().noun }}. }
          @else { I reviewed this exact task, engagement scope, revision, and reason. }
        </label>
        <div class="actions">
          <button matButton="filled" type="button" (click)="submit()" [disabled]="busy() || !assent()">
            {{ copy().confirm }}
          </button>
          @if (state() === 'review') { <button matButton type="button" (click)="backToEditing()" [disabled]="busy()">Back to reason</button> }
        </div>
      }

      @if (busy()) { <p role="status">Waiting for the {{ copy().noun }} response. The reviewed reason and revision are held unchanged.</p> }
      @if (state() === 'cleanup') {
        <p role="alert">{{ message() }}</p>
        <button matButton type="button" (click)="retryUnsentCleanup()" [disabled]="busy()">Retry clearing unsent recovery reference</button>
      }
      @if (state() === 'finalizing') {
        <p role="alert">{{ message() }}</p>
        <button matButton type="button" (click)="retryDefinitiveCleanup()" [disabled]="busy()">Retry clearing completed recovery reference</button>
      }
      @if (state() === 'unknown') {
        <p role="alert">{{ message() || ('The ' + copy().noun + ' outcome is unconfirmed.') }} The exact original reason and revision are retained. Do not resend until the current task has been checked.</p>
        <button matButton type="button" (click)="checkStatus()" [disabled]="busy()">Check {{ copy().noun }} status</button>
      }
      @if (state() === 'observed') {
        @if (recoveryRegistry()) {
          <p role="status">The task revision advanced to {{ observed()?.revision }}. This observation does not establish the original request outcome.</p>
          @if (actionMode() === 'reopen') {
            <p>This does not confirm who reopened the task or which reason was retained. Review task activity before relying on the change.</p>
            <button matButton="filled" type="button" (click)="acknowledgeAndReviewActivity()">Acknowledge reopening observation and review activity</button>
          } @else {
            <button matButton="filled" type="button" (click)="acknowledgeAndReviewActivity()">Acknowledge {{ copy().noun }} and review activity</button>
          }
        } @else if (actionMode() === 'suspend' || actionMode() === 'resume') {
          <p role="status">{{ actionMode() === 'suspend' ? 'Hold' : 'Resumption' }} observed at task revision {{ observed()?.revision }}. Review the retained activity and reason before relying on it.</p>
          <button matButton="filled" type="button" (click)="acknowledgeAndReviewActivity()">Acknowledge {{ copy().noun }} and review activity</button>
        } @else if (actionMode() === 'cancel') {
          <p role="status">Cancellation observed at task revision {{ observed()?.revision }}. Review the retained activity and reason before relying on it.</p>
          <button matButton="filled" type="button" (click)="acknowledgeAndReviewActivity()">Acknowledge cancellation and review activity</button>
        } @else {
          <p role="status">An active task status is observed at revision {{ observed()?.revision }}. This does not confirm who reopened the task or which reason was retained. Review task activity before relying on the change.</p>
          <button matButton="filled" type="button" (click)="acknowledgeAndReviewActivity()">Acknowledge reopening observation and review activity</button>
        }
      }
      @if (state() === 'stale') {
        <p role="alert">The task now has status {{ observed()?.status }} at revision {{ observed()?.revision }}. The original revision changed, so this {{ copy().noun }} cannot be retried. This observation does not establish the original request outcome.</p>
        <button matButton="filled" type="button" (click)="acknowledgeChangedTask()" [disabled]="busy()">Acknowledge changed task and refresh</button>
      }
      @if (state() === 'confirmed') {
        <p role="status">Task {{ copy().noun }} response confirmed. Review the retained task activity and {{ copy().noun }} reason.</p>
        <button matButton="filled" type="button" (click)="reviewActivity.emit()">Review {{ copy().noun }} activity</button>
      }
      @if (state() === 'refused') {
        <p role="alert">{{ message() }}</p>
        <button matButton type="button" (click)="refreshRequested.emit()">Refresh work queue</button>
      }
      @if (failed() && state() !== 'refused' && state() !== 'unknown' && state() !== 'cleanup' && state() !== 'finalizing') { <p role="alert">{{ message() }}</p> }
      @if (state() === 'editing' || state() === 'review' || state() === 'refused') {
        <button matButton type="button" (click)="closeRequested.emit()" [disabled]="busy()">Close {{ copy().noun }} review</button>
      }
    </section>
  `,
  styles: `:host { display: block; }
    .panel { margin-block: 1rem; padding: 1rem; border: 1px solid var(--as-line, #d8e1eb); border-radius: .5rem; }
    dl { display: grid; grid-template-columns: minmax(9rem, max-content) minmax(0, 1fr); gap: .35rem 1rem; }
    dd { margin: 0; overflow-wrap: anywhere; } dt { font-weight: 600; }
    textarea { display: block; box-sizing: border-box; width: min(100%, 42rem); min-width: 0; max-width: 100%; margin-block: .5rem; }
    .reason { white-space: pre-wrap; overflow-wrap: anywhere; }
    .assent { display: flex; align-items: flex-start; gap: .5rem; margin-block: 1rem; }
    .actions { display: flex; flex-wrap: wrap; gap: .5rem; } p, small { overflow-wrap: anywhere; }
    @media (max-width: 600px) { dl { grid-template-columns: minmax(0, 1fr); } dd { margin-bottom: .5rem; } }`,
})
export class WorkflowTaskCancellationPanel {
  readonly task = input.required<WorkflowTaskCancellationTarget>();
  readonly action = input<WorkflowTaskRecoveryAction>('cancel');
  readonly readable = input(true);
  readonly accessCurrent = input<() => boolean>(() => true);
  readonly accessRevision = input<() => number>(() => 0);
  /** Opt in after the parent can exempt this panel's exact active fence from its access callback. */
  readonly recoveryRegistry = input<WorkQueueActionRegistry | null>(null);
  /** Exact issuing ticket identity, including while its persisted record is absent during finalization. */
  readonly activeRegistryRequest = signal<WorkQueuePendingAction | null>(null);
  readonly registryChanged = output<void>();
  readonly scopeCheckFailed = output<void>();
  readonly changed = output<void>();
  readonly reviewActivity = output<void>();
  readonly refreshRequested = output<void>();
  readonly closeRequested = output<void>();

  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly hostElement = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly renderInjector = inject(Injector);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('panelHeading');
  private readonly reasonEditor = viewChild<ElementRef<HTMLTextAreaElement>>('reasonEditor');
  readonly state = signal<RecoveryState>('editing');
  readonly intent = signal<Intent | null>(null);
  readonly actionMode = computed(() => this.intent()?.action ?? this.action());
  readonly copy = computed(() => RECOVERY_COPY[this.actionMode()]);
  /** Task status when the intent was reviewed; a retry is offered only while the task still has it. */
  private readonly reviewedStatus = signal<string | null>(null);
  readonly reopensCompletedWork = computed(() => this.actionMode() === 'reopen' &&
    ((this.intent() ? this.reviewedStatus() : null) ?? this.task().taskStatus) === 'COMPLETED');
  readonly canMarkNotApplicable = computed(() => this.actionMode() === 'cancel' &&
    this.task().requiredTask === true && !!this.task().applicabilityCondition?.trim());
  readonly notApplicable = signal(false);
  readonly observed = signal<Workspace['tasks'][number] | null>(null);
  readonly assent = signal(false);
  readonly busy = signal(false);
  readonly failed = signal(false);
  readonly message = signal('');
  reason = '';

  private owner = '';
  private context = '';
  private request = 0;
  private destroyed = false;
  private focusInteraction = 0;
  private registryOwner: WorkQueueRegistryOwner | null = null;
  private registryAtDispatch: WorkQueueActionRegistry | null = null;
  private dispatchTicket: WorkQueueDispatchTicket | null = null;
  private dispatchIntent: Readonly<Intent> | null = null;
  private definitiveOutcome: { confirmed: boolean; message: string } | null = null;
  private persistentContext = false;
  private observationOwner = '';
  private observationContext = '';
  private observationRevision = -1;

  constructor() {
    effect(() => {
      const task = this.task();
      const owner = this.ownerKey();
      const context = [task.taskId, task.clientId, task.engagementId, task.title, task.engagementServiceRoute,
        task.engagementPeriod, task.revision, task.taskStatus, task.canCancel, task.canReopen, task.canSuspend, task.canResume,
      task.requiredTask, task.applicabilityCondition, this.action()].join('|');
      if (owner === this.owner && context === this.context) return;
      this.owner = owner;
      this.context = context;
      this.reset();
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; ++this.request; });
  }

  @HostListener('document:pointerdown')
  @HostListener('document:keydown')
  recordFocusInteraction(): void { this.focusInteraction++; }

  focusReview(): void { this.heading()?.nativeElement.focus(); }

  /** Keep the original intent, but require a fresh observation after authority changes. */
  invalidateAvailability(): void {
    if (this.state() === 'cleanup' || this.state() === 'finalizing') { this.assent.set(false); return; }
    if (!this.busy() && ['editing', 'review'].includes(this.state())) { this.reset(); return; }
    if (!this.intent() || (!this.busy() && !['unknown', 'retryable', 'observed', 'stale'].includes(this.state()))) return;
    ++this.request;
    this.busy.set(false);
    this.observed.set(null);
    this.assent.set(false);
    this.state.set('unknown');
    this.failed.set(true);
    this.message.set('Access changed while the outcome was unresolved. Check the current task again before continuing.');
  }

  hasUnresolvedOutcome(): boolean {
    return this.busy() || ['cleanup', 'finalizing', 'unknown', 'retryable', 'observed', 'stale'].includes(this.state());
  }

  updateReason(value: string): void {
    if (!this.busy() && this.state() === 'editing') this.reason = value;
  }

  prepareReview(): void {
    const task = this.task();
    const action = this.action();
    if (!this.canPerform(task, action) || this.state() !== 'editing' || this.busy()) return;
    this.failed.set(false);
    this.message.set('');
    const justification = this.reason.trim();
    const reason = justification && this.canMarkNotApplicable() && this.notApplicable()
      ? `Not applicable: ${justification}` : justification;
    if (!justification || reason.length > 2000) {
      this.failed.set(true);
      this.message.set(`Enter a non-empty ${RECOVERY_COPY[action].noun} reason of up to 2,000 characters.`);
      return;
    }
    const origin = this.focusedControl();
    const owner = this.ownerKey();
    const context = this.contextKey();
    this.intent.set({ action, expectedRevision: task.revision, reason });
    this.reviewedStatus.set(task.taskStatus);
    this.assent.set(false);
    this.state.set('review');
    if (origin) this.focusAfterRender(() => this.heading()?.nativeElement, owner, context, origin);
  }

  backToEditing(): void {
    if (this.busy() || this.state() !== 'review') return;
    const origin = this.focusedControl();
    const owner = this.ownerKey();
    const context = this.contextKey();
    this.intent.set(null);
    this.assent.set(false);
    this.state.set('editing');
    if (origin) this.focusAfterRender(() => this.reasonEditor()?.nativeElement, owner, context, origin);
  }

  async submit(): Promise<void> {
    const intent = this.intent();
    const state = this.state();
    const task = this.task();
    if (!this.readable() || !this.accessCurrent()() || !intent || !this.assent() || this.busy() || !['review', 'retryable'].includes(state) ||
        intent.action !== this.action() || state === 'review' && !this.canPerform(task, intent.action)) return;
    const registry = this.recoveryRegistry();
    if (this.persistentContext && (!registry || this.registryAtDispatch && registry !== this.registryAtDispatch)) {
      this.failed.set(true); this.message.set('The recovery reference is unavailable. No action was sent.'); return;
    }
    if (registry) this.persistentContext = true;
    if (registry && state === 'retryable' && (!this.dispatchTicket || !this.dispatchIntent ||
        this.dispatchIntent.action !== intent.action || this.dispatchIntent.expectedRevision !== intent.expectedRevision ||
        this.dispatchIntent.reason !== intent.reason || this.observationOwner !== this.ownerKey() ||
        this.observationContext !== this.contextKey() || this.observationRevision !== this.accessRevision()() ||
        this.observed()?.revision !== intent.expectedRevision || !this.canPerform(task, intent.action))) {
      this.invalidateAvailability(); return;
    }
    const owner = this.ownerKey();
    const context = this.contextKey();
    const origin = this.focusedControl();
    const request = ++this.request;
    const accessRevision = this.accessRevision()();
    const url = this.actionUrl(task, intent.action);
    const expectedRevision = intent.expectedRevision, reason = intent.reason;
    this.busy.set(true);
    this.failed.set(false);
    this.message.set('');
    try {
      let ticket: WorkQueueDispatchTicket | null = null;
      if (registry && state === 'review') {
        const actor = this.session.current();
        if (!actor?.staff) { this.failed.set(true); this.message.set('The current session is unavailable. No action was sent.'); return; }
        const registryOwner: WorkQueueRegistryOwner = { firmId: actor.firmId, userId: actor.userId,
          generation: actor.generation, invalidation: this.session.invalidation(), staff: true };
        let requestId: string;
        try { requestId = crypto.randomUUID(); }
        catch { this.failed.set(true); this.message.set('A recovery reference could not be created. No action was sent.'); return; }
        const reservation = registry.reserveForDispatchRecoverable(registryOwner, { engagementId: task.engagementId, taskId: task.taskId,
          expectedRevision, action: intent.action, requestedDueDate: null, requestId });
        if (!reservation.ok) {
          if ('cleanup' in reservation) {
            ticket = reservation.cleanup;
            this.dispatchTicket = ticket;
            this.dispatchIntent = Object.freeze({ action: intent.action, expectedRevision, reason });
            this.registryOwner = registryOwner; this.registryAtDispatch = registry;
            this.activeRegistryRequest.set({ ...ticket.item });
            this.cleanupUnsentReservation(registry, ticket, owner, context, request);
            return;
          }
          if (this.current(owner, context, request)) {
            this.registryChanged.emit(); this.failed.set(true);
            this.message.set(reservation.reason === 'reconciliation_pending'
              ? 'Another recovery reference needs reconciliation. No action was sent.'
              : 'The recovery reference could not be secured. No action was sent.');
          }
          return;
        }
        ticket = reservation.value;
        this.dispatchTicket = ticket;
        this.dispatchIntent = Object.freeze({ action: intent.action, expectedRevision, reason });
        this.registryOwner = registryOwner;
        this.registryAtDispatch = registry;
        this.activeRegistryRequest.set({ ...ticket.item });
        this.registryChanged.emit();
        if (!this.current(owner, context, request) || this.recoveryRegistry() !== registry || !this.readable() || !this.accessCurrent()() || accessRevision !== this.accessRevision()()) {
          this.cleanupUnsentReservation(registry, ticket, owner, context, request);
          return;
        }
      }
      const send = () => this.api.command<unknown>(url, { expectedRevision, reason });
      let response: Promise<Awaited<ReturnType<typeof send>>>;
      if (registry && state === 'retryable') {
        ticket = this.dispatchTicket;
        if (!ticket) return;
        const invoked = registry.invokeExactRetry<Awaited<ReturnType<typeof send>>>(ticket, () =>
          this.current(owner, context, request) && this.recoveryRegistry() === registry && this.registryAtDispatch === registry &&
          this.activeRegistryRequest()?.requestId === ticket!.item.requestId && this.readable() && this.accessCurrent()() &&
          accessRevision === this.accessRevision()() && this.observationOwner === owner && this.observationContext === context &&
          this.observationRevision === accessRevision && this.observed()?.revision === expectedRevision &&
          this.state() === 'retryable' && this.assent() && this.canPerform(this.task(), intent.action) &&
          this.intent()?.action === this.dispatchIntent?.action && this.intent()?.expectedRevision === this.dispatchIntent?.expectedRevision &&
          this.intent()?.reason === this.dispatchIntent?.reason);
        this.assent.set(false);
        if (!invoked.ok) {
          if (this.current(owner, context, request)) {
            this.state.set('unknown'); this.failed.set(true);
            this.message.set('The retry was not sent. Check the current task and access again.');
          }
          return;
        }
        response = invoked.value;
      } else if (registry && ticket) {
        const invoked = registry.invokeReserved(ticket, send);
        this.assent.set(false);
        if (!invoked.ok) {
          if (invoked.reason === 'storage_unavailable' || invoked.reason === 'invalid_storage' || invoked.reason === 'reconciliation_pending') {
            this.cleanupUnsentReservation(registry, ticket, owner, context, request);
            return;
          }
          if (this.current(owner, context, request)) {
            this.registryChanged.emit(); this.state.set('unknown'); this.failed.set(true);
            this.message.set('The action and its recovery reference could not be verified.');
          }
          return;
        }
        response = invoked.value;
      } else { this.assent.set(false); response = send(); }
      const outcome = await response;
      if (!this.current(owner, context, request)) return;
      if (registry && this.recoveryRegistry() !== registry) { this.invalidateAvailability(); return; }
      if (!this.accessCurrent()() || accessRevision !== this.accessRevision()()) { this.invalidateAvailability(); return; }
      if (registry && ticket && this.registryOwner && (outcome.ok && outcome.value === true ||
          state === 'review' && !outcome.ok && !outcome.unknown)) {
        this.definitiveOutcome = outcome.ok ? { confirmed: true, message: '' } : { confirmed: false, message: outcome.message };
        const removed = registry.removeDefinitiveTicket(ticket);
        if (!this.current(owner, context, request)) return;
        if (!this.readable() || !this.accessCurrent()() || accessRevision !== this.accessRevision()()) {
          this.state.set('finalizing'); this.failed.set(true);
          this.message.set('Access changed. Check the recovery reference when access is current.'); return;
        }
        if (!removed.ok) {
          this.state.set('finalizing'); this.failed.set(true);
          this.message.set('The response was received, but its recovery reference could not be cleared. Retry clearing it when storage is available.');
          this.registryChanged.emit(); return;
        }
        this.registryChanged.emit();
        if (!this.current(owner, context, request)) return;
        if (!this.readable() || !this.accessCurrent()() || accessRevision !== this.accessRevision()()) {
          this.state.set('finalizing'); this.failed.set(true);
          this.message.set('Access changed. Check the recovery reference when access is current.'); return;
        }
        const finished = registry.finishDefinitiveTicket(ticket);
        if (!finished.ok) {
          this.state.set('finalizing'); this.failed.set(true);
          this.message.set('The recovery reference could not be confirmed. Retry when access and storage are current.'); return;
        }
        if (!this.current(owner, context, request)) return;
        this.activeRegistryRequest.set(null); this.registryOwner = null; this.registryAtDispatch = null;
        this.dispatchTicket = null; this.dispatchIntent = null; this.definitiveOutcome = null;
        this.registryChanged.emit();
        if (!this.current(owner, context, request) || !this.readable() || !this.accessCurrent()() || accessRevision !== this.accessRevision()()) return;
      }
      if (outcome.ok && outcome.value === true) {
        this.state.set('confirmed');
        this.changed.emit();
      } else if (outcome.ok || outcome.unknown || registry && state === 'retryable') {
        this.state.set('unknown');
        this.failed.set(true);
        this.message.set(`The ${RECOVERY_COPY[intent.action].noun} response could not be confirmed.`);
        if (origin) this.focusAfterRender(() => this.heading()?.nativeElement, owner, context, origin);
      } else {
        this.state.set('refused');
        this.failed.set(true);
        this.message.set(outcome.message);
        this.refreshRequested.emit();
        if (origin) this.focusAfterRender(() => this.heading()?.nativeElement, owner, context, origin);
      }
      if (outcome.ok && outcome.value === true && origin)
        this.focusAfterRender(() => this.heading()?.nativeElement, owner, context, origin);
    } catch {
      if (!this.current(owner, context, request)) return;
      if (!this.accessCurrent()() || accessRevision !== this.accessRevision()()) { this.invalidateAvailability(); return; }
      this.state.set('unknown');
      this.failed.set(true);
      this.message.set(`The ${RECOVERY_COPY[intent.action].noun} response could not be confirmed.`);
      if (origin) this.focusAfterRender(() => this.heading()?.nativeElement, owner, context, origin);
    } finally {
      if (this.current(owner, context, request)) this.busy.set(false);
    }
  }

  /** Retry only the exact local removal after a definitive response; never resend the command. */
  retryDefinitiveCleanup(): void {
    const registry = this.recoveryRegistry(), ticket = this.dispatchTicket, outcome = this.definitiveOutcome;
    if (this.destroyed || this.busy() || this.state() !== 'finalizing' || !registry || registry !== this.registryAtDispatch ||
        !ticket || !outcome || this.activeRegistryRequest()?.requestId !== ticket.item.requestId ||
        this.ownerKey() !== this.owner || this.contextKey() !== this.context || !this.readable() || !this.accessCurrent()()) return;
    const owner = this.ownerKey(), context = this.contextKey(), request = this.request, revision = this.accessRevision()();
    const removed = registry.removeDefinitiveTicket(ticket);
    if (!this.current(owner, context, request)) return;
    if (!this.readable() || !this.accessCurrent()() || revision !== this.accessRevision()()) { this.invalidateAvailability(); return; }
    if (!removed.ok) {
      this.failed.set(true); this.message.set('The recovery reference could not be cleared. Check access and storage before retrying.');
      this.registryChanged.emit(); return;
    }
    this.registryChanged.emit();
    if (!this.current(owner, context, request)) return;
    if (!this.readable() || !this.accessCurrent()() || revision !== this.accessRevision()()) { this.invalidateAvailability(); return; }
    const finished = registry.finishDefinitiveTicket(ticket);
    if (!this.current(owner, context, request)) return;
    if (!finished.ok) {
      this.failed.set(true); this.message.set('The recovery reference could not be confirmed. Check access and storage before retrying.'); return;
    }
    this.activeRegistryRequest.set(null); this.registryOwner = null; this.registryAtDispatch = null;
    this.dispatchTicket = null; this.dispatchIntent = null; this.definitiveOutcome = null;
    this.registryChanged.emit();
    if (!this.current(owner, context, request) || !this.readable() || !this.accessCurrent()() || revision !== this.accessRevision()()) return;
    this.failed.set(!outcome.confirmed);
    this.state.set(outcome.confirmed ? 'confirmed' : 'refused');
    this.message.set(outcome.message);
    if (outcome.confirmed) this.changed.emit(); else this.refreshRequested.emit();
  }

  /** Retry local cleanup only; this never invokes the command. */
  retryUnsentCleanup(): void {
    const registry = this.recoveryRegistry(), ticket = this.dispatchTicket;
    if (this.destroyed || this.busy() || this.state() !== 'cleanup' || !registry || registry !== this.registryAtDispatch ||
        !ticket || this.activeRegistryRequest()?.requestId !== ticket.item.requestId ||
        this.ownerKey() !== this.owner || this.contextKey() !== this.context || !this.readable() || !this.accessCurrent()()) return;
    this.cleanupUnsentReservation(registry, ticket, this.ownerKey(), this.contextKey(), this.request);
  }

  private cleanupUnsentReservation(registry: WorkQueueActionRegistry, ticket: WorkQueueDispatchTicket,
    owner: string, context: string, request: number): void {
    const aborted = registry.abortBeforeDispatch(ticket);
    if (!this.current(owner, context, request)) return;
    this.assent.set(false);
    this.failed.set(true);
    if (aborted.ok) {
      this.activeRegistryRequest.set(null); this.registryOwner = null; this.registryAtDispatch = null;
      this.dispatchTicket = null; this.dispatchIntent = null;
      this.state.set('review');
      this.message.set('No action was sent. The recovery reference was cleared; review and confirm again before sending.');
    } else if (aborted.reason === 'dispatch_started') {
      this.state.set('unknown');
      this.message.set('The request outcome and recovery reference could not be verified.');
    } else {
      this.state.set('cleanup');
      this.message.set('No action was sent. The recovery reference could not be cleared; retry clearing it when storage is available.');
    }
    this.registryChanged.emit();
  }

  async checkStatus(): Promise<void> {
    const intent = this.intent();
    const task = this.task();
    if (!this.readable() || !this.accessCurrent()() || !intent || this.state() !== 'unknown' || this.busy()) return;
    if (this.persistentContext && (!this.recoveryRegistry() || this.registryAtDispatch && this.recoveryRegistry() !== this.registryAtDispatch)) return;
    const accessRevision = this.accessRevision()();
    const owner = this.ownerKey();
    const context = this.contextKey();
    const origin = this.focusedControl();
    const request = ++this.request;
    this.busy.set(true);
    this.failed.set(false);
    this.message.set('');
    this.observed.set(null);
    try {
      const workspace = await this.api.get(this.workspaceUrl(task), decodeWorkflowTaskWorkspace);
      if (!this.current(owner, context, request)) return;
      if (!this.accessCurrent()() || accessRevision !== this.accessRevision()()) { this.invalidateAvailability(); return; }
      const row = this.exactObservation(workspace, task.taskId, task.engagementId, intent.expectedRevision);
      this.observationOwner = owner; this.observationContext = context; this.observationRevision = accessRevision;
      if (intent.action === 'suspend' || intent.action === 'resume') {
        const target = intent.action === 'suspend';
        if (row.onHold === target && row.revision > intent.expectedRevision) this.state.set('observed');
        else if (row.onHold === !target && row.revision === intent.expectedRevision && ['OPEN', 'IN_PROGRESS'].includes(row.status)) {
          this.state.set('retryable');
          this.assent.set(false);
        } else this.state.set('stale');
      } else if (intent.action === 'cancel') {
        if (row.status === 'CANCELLED') {
          if (row.revision <= intent.expectedRevision)
            throw new Error('Cancellation status did not advance the reviewed revision. Keep the outcome unresolved and refresh before relying on it.');
          this.state.set('observed');
        } else if (row.revision === intent.expectedRevision && ['OPEN', 'IN_PROGRESS'].includes(row.status)) {
          this.state.set('retryable');
          this.assent.set(false);
        } else this.state.set('stale');
      } else if (['OPEN', 'IN_PROGRESS'].includes(row.status) && row.revision > intent.expectedRevision) {
        this.state.set('observed');
      } else if (row.status === this.reviewedStatus() && row.revision === intent.expectedRevision) {
        this.state.set('retryable');
        this.assent.set(false);
      } else this.state.set('stale');
      const registry = this.recoveryRegistry();
      if (registry && this.state() === 'retryable') {
        const readiness = this.dispatchTicket && registry === this.registryAtDispatch
          ? registry.retryReadiness(this.dispatchTicket) : null;
        if (!this.current(owner, context, request)) return;
        if (this.recoveryRegistry() !== registry || !this.readable() || !this.accessCurrent()() || accessRevision !== this.accessRevision()()) {
          this.invalidateAvailability(); return;
        }
        if (!readiness?.ok || readiness.value !== 'settled') {
          this.state.set('unknown');
          this.assent.set(false);
          this.failed.set(true);
          this.message.set(readiness?.ok && readiness.value === 'in_flight'
            ? 'The task is unchanged, but a request for this action is still awaiting a response. Check the task again later.'
            : 'The retry reference could not be verified. Check the task and access again.');
        }
      }
      this.observed.set(row);
      if (origin) this.focusAfterRender(() => this.heading()?.nativeElement, owner, context, origin);
    } catch (error) {
      if (!this.current(owner, context, request)) return;
      this.failed.set(true);
      const invalidRevision = 'Cancellation status did not advance the reviewed revision. Keep the outcome unresolved and refresh before relying on it.';
      this.message.set(error instanceof Error && error.message === invalidRevision ? invalidRevision : 'The current task could not be verified. No request was sent.');
      this.scopeCheckFailed.emit();
      if (origin) this.focusAfterRender(() => this.heading()?.nativeElement, owner, context, origin);
    } finally {
      if (this.current(owner, context, request)) this.busy.set(false);
    }
  }

  acknowledgeAndReviewActivity(): void {
    if (this.state() !== 'observed' || !this.observed()) return;
    if (!this.acknowledgeRegistryObservation()) return;
    this.reviewActivity.emit();
  }

  acknowledgeChangedTask(): void {
    if (this.state() !== 'stale' || !this.observed()) return;
    if (!this.acknowledgeRegistryObservation()) return;
    this.refreshRequested.emit();
    this.closeRequested.emit();
  }

  private acknowledgeRegistryObservation(): boolean {
    const registry = this.recoveryRegistry();
    if (!registry) return !this.persistentContext;
    const item = this.activeRegistryRequest(), owner = this.registryOwner, row = this.observed();
    if (!item || !owner || !row || registry !== this.registryAtDispatch || !this.readable() || !this.accessCurrent()() ||
        this.observationOwner !== this.ownerKey() || this.observationContext !== this.contextKey() ||
        this.observationRevision !== this.accessRevision()()) return false;
    const ownerKey = this.ownerKey(), context = this.contextKey(), request = this.request, revision = this.accessRevision()();
    const observation = { owner, engagementId: item.engagementId, taskId: item.taskId,
      revision: row.revision, availabilityRevision: this.observationRevision };
    const pending = registry.pendingReconciliation(owner);
    if (!pending.ok) { this.failed.set(true); this.message.set('The current task observation could not be acknowledged.'); return false; }
    const matching = pending.value?.kind === 'acknowledgement' && pending.value.sameSession &&
      pending.value.item.requestId === item.requestId ? pending.value : null;
    const result = matching?.ticket
      ? registry.reconcileAcknowledgement(owner, matching.ticket, observation, this.accessRevision()())
      : pending.value ? { ok: false as const } : registry.acknowledge(owner, item.requestId, observation, this.accessRevision()());
    if (!this.current(ownerKey, context, request) || !this.readable() || !this.accessCurrent()() || revision !== this.accessRevision()()) {
      this.scopeCheckFailed.emit(); this.registryChanged.emit(); return false;
    }
    if (!result.ok) {
      this.observed.set(null); this.state.set('unknown'); this.assent.set(false);
      this.failed.set(true); this.message.set('The observation could not be acknowledged. Check the current task again.');
      this.scopeCheckFailed.emit(); this.registryChanged.emit(); return false;
    }
    this.activeRegistryRequest.set(null); this.registryOwner = null; this.registryAtDispatch = null;
    this.dispatchTicket = null; this.dispatchIntent = null; this.registryChanged.emit();
    return true;
  }

  private exactObservation(workspace: Workspace, taskId: string, engagementId: string, expectedRevision: number): Workspace['tasks'][number] {
    if (workspace.engagementId !== engagementId) throw new Error('The task workspace did not match the reviewed engagement. Keep the original request unresolved.');
    const matches = workspace.tasks.filter(task => task.id === taskId);
    if (matches.length !== 1 || matches[0].revision < expectedRevision)
      throw new Error('The exact current task could not be verified. Keep the original request unresolved; no request was resent.');
    return matches[0];
  }

  private reset(): void {
    ++this.request;
    this.state.set('editing');
    this.intent.set(null);
    this.reviewedStatus.set(null);
    this.observed.set(null);
    this.assent.set(false);
    this.notApplicable.set(false);
    this.busy.set(false);
    this.failed.set(false);
    this.message.set('');
    this.reason = '';
    this.activeRegistryRequest.set(null); this.registryOwner = null; this.registryAtDispatch = null;
    this.dispatchTicket = null; this.dispatchIntent = null;
    this.definitiveOutcome = null;
    this.persistentContext = false;
    this.observationOwner = ''; this.observationContext = ''; this.observationRevision = -1;
  }

  private current(owner: string, context: string, request: number): boolean {
    return !this.destroyed && owner === this.ownerKey() && context === this.contextKey() && request === this.request;
  }

  private focusedControl(): FocusOrigin | null {
    const host = this.hostElement.nativeElement;
    const active = host.ownerDocument.activeElement;
    return active && host.contains(active) ? { element: active, interaction: this.focusInteraction } : null;
  }

  private focusAfterRender(target: () => HTMLElement | undefined, owner: string, context: string, origin: FocusOrigin): void {
    afterNextRender(() => {
      const document = this.hostElement.nativeElement.ownerDocument;
      if (this.destroyed || owner !== this.ownerKey() || context !== this.contextKey() ||
          origin.interaction !== this.focusInteraction ||
          (document.activeElement !== origin.element && document.activeElement !== document.body)) return;
      target()?.focus();
    }, { injector: this.renderInjector });
  }

  private ownerKey(): string {
    const current = this.session.current();
    return [current?.firmId, current?.userId, current?.generation, current?.staff, this.session.invalidation()].join('|');
  }

  private contextKey(): string {
    const task = this.task();
    return [task.taskId, task.clientId, task.engagementId, task.title, task.engagementServiceRoute,
      task.engagementPeriod, task.revision, task.taskStatus, task.canCancel, task.canReopen, task.canSuspend, task.canResume,
      task.requiredTask, task.applicabilityCondition, this.action()].join('|');
  }

  canPerform(task: WorkflowTaskCancellationTarget, action: WorkflowTaskRecoveryAction): boolean {
    return action === 'cancel' ? task.canCancel : action === 'reopen' ? canReopenTask(task) :
      action === 'suspend' ? task.canSuspend === true : task.canResume === true;
  }

  private actionUrl(task: WorkflowTaskCancellationTarget, action: WorkflowTaskRecoveryAction): string {
    return `/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/${action}`;
  }

  private workspaceUrl(task: WorkflowTaskCancellationTarget): string {
    return `/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${encodeURIComponent(task.taskId)}`;
  }
}
