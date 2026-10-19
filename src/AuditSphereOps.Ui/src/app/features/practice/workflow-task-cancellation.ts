import { afterNextRender, Component, DestroyRef, ElementRef, effect, HostListener, inject, Injector, input, output, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, guid, int, nullable, obj, str } from '../../core/decode';
import { SessionService } from '../../core/session';

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
  }), 500),
});

/** Exact scoped task workspace used for post-timeout observation; it grants no mutation authority. */
export const decodeWorkflowTaskWorkspace = workflowTaskWorkspaceShape;
type Workspace = ReturnType<typeof decodeWorkflowTaskWorkspace>;
type Intent = { expectedRevision: number; reason: string };
type RecoveryState = 'editing' | 'review' | 'unknown' | 'retryable' | 'observed' | 'stale' | 'confirmed' | 'refused';
type FocusOrigin = { element: Element; interaction: number };

@Component({
  selector: 'audit-workflow-task-cancellation',
  imports: [MatButtonModule],
  template: `
    <section class="panel" aria-labelledby="cancellation-heading">
      <h2 #panelHeading id="cancellation-heading" tabindex="-1">Review task cancellation</h2>
      <p>Cancellation only ends this operational task. It does not approve or complete source audit work. A cancelled required predecessor continues to block its dependent task.</p>
      <dl>
        <dt>Task</dt><dd>{{ task().title }} · {{ task().taskId }}</dd>
        <dt>Client</dt><dd>{{ task().clientName }} · {{ task().clientId }}</dd>
        <dt>Engagement scope</dt><dd>{{ task().engagementServiceRoute }} · {{ task().engagementPeriod }} · {{ task().engagementId }}</dd>
        <dt>Current task revision</dt><dd>{{ task().revision }}</dd>
      </dl>

      @if (state() === 'editing') {
        <label for="task-cancellation-reason">Reason for cancellation</label>
        <textarea #reasonEditor id="task-cancellation-reason" rows="4" maxlength="2000" [value]="reason"
          [disabled]="busy()" (input)="updateReason($any($event.target).value)"
          aria-describedby="task-cancellation-reason-help"></textarea>
        <small id="task-cancellation-reason-help">Enter a reason of up to 2,000 characters. It will be retained in task activity.</small>
        <button matButton="filled" type="button" (click)="prepareReview()" [disabled]="busy() || !task().canCancel">Review cancellation</button>
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
          <p role="status">The task is still active at the original revision. No cancellation is observed. A retry will send the exact original reason and revision.</p>
        }
        <label class="assent"><input type="checkbox" [checked]="assent()" [disabled]="busy()"
          (change)="assent.set($any($event.target).checked)" />
          @if (state() === 'retryable') { I reviewed the unchanged task and exact original reason, and explicitly agree to retry this cancellation. }
          @else { I reviewed this exact task, engagement scope, revision, and reason. }
        </label>
        <div class="actions">
          <button matButton="filled" type="button" (click)="submit()" [disabled]="busy() || !assent()">Confirm task cancellation</button>
          @if (state() === 'review') { <button matButton type="button" (click)="backToEditing()" [disabled]="busy()">Back to reason</button> }
        </div>
      }

      @if (busy()) { <p role="status">Waiting for the cancellation response. The reviewed reason and revision are held unchanged.</p> }
      @if (state() === 'unknown') {
        <p role="alert">{{ message() || 'The cancellation outcome is unconfirmed.' }} The exact original reason and revision are retained. Do not resend until the current task has been checked.</p>
        <button matButton type="button" (click)="checkStatus()" [disabled]="busy()">Check cancellation status</button>
      }
      @if (state() === 'observed') {
        <p role="status">Cancellation observed at task revision {{ observed()?.revision }}. Review the retained activity and reason before relying on it.</p>
        <button matButton="filled" type="button" (click)="acknowledgeAndReviewActivity()">Acknowledge cancellation and review activity</button>
      }
      @if (state() === 'stale') {
        <p role="alert">The task now has status {{ observed()?.status }} at revision {{ observed()?.revision }}. The original revision changed, so this cancellation cannot be retried. Refresh the queue and review the current task.</p>
        <button matButton="filled" type="button" (click)="acknowledgeChangedTask()" [disabled]="busy()">Acknowledge changed task and refresh</button>
      }
      @if (state() === 'confirmed') {
        <p role="status">Task cancellation confirmed. Review the retained task activity and cancellation reason.</p>
        <button matButton="filled" type="button" (click)="reviewActivity.emit()">Review cancellation activity</button>
      }
      @if (state() === 'refused') {
        <p role="alert">{{ message() }}</p>
        <button matButton type="button" (click)="refreshRequested.emit()">Refresh work queue</button>
      }
      @if (failed() && state() !== 'refused' && state() !== 'unknown') { <p role="alert">{{ message() }}</p> }
      @if (state() === 'editing' || state() === 'review' || state() === 'refused') {
        <button matButton type="button" (click)="closeRequested.emit()" [disabled]="busy()">Close cancellation review</button>
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

  constructor() {
    effect(() => {
      const task = this.task();
      const owner = this.ownerKey();
      const context = [task.taskId, task.clientId, task.engagementId, task.title, task.engagementServiceRoute,
        task.engagementPeriod, task.revision, task.taskStatus, task.canCancel].join('|');
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

  updateReason(value: string): void {
    if (!this.busy() && this.state() === 'editing') this.reason = value;
  }

  prepareReview(): void {
    const task = this.task();
    if (!task.canCancel || this.state() !== 'editing' || this.busy()) return;
    this.failed.set(false);
    this.message.set('');
    const reason = this.reason.trim();
    if (!reason || reason.length > 2000) {
      this.failed.set(true);
      this.message.set('Enter a non-empty cancellation reason of up to 2,000 characters.');
      return;
    }
    const origin = this.focusedControl();
    const owner = this.ownerKey();
    const context = this.contextKey();
    this.intent.set({ expectedRevision: task.revision, reason });
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
    if (!intent || !this.assent() || this.busy() || !['review', 'retryable'].includes(state) ||
        state === 'review' && !task.canCancel) return;
    const owner = this.ownerKey();
    const context = this.contextKey();
    const origin = this.focusedControl();
    const request = ++this.request;
    const url = this.cancelUrl(task);
    this.busy.set(true);
    this.failed.set(false);
    this.message.set('');
    this.assent.set(false);
    try {
      const outcome = await this.api.command<unknown>(url, {
        expectedRevision: intent.expectedRevision,
        reason: intent.reason,
      });
      if (!this.current(owner, context, request)) return;
      if (outcome.ok && outcome.value === true) {
        this.state.set('confirmed');
        this.changed.emit();
      } else if (outcome.ok || outcome.unknown) {
        this.state.set('unknown');
        this.failed.set(true);
        this.message.set('The cancellation response could not be confirmed.');
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
      this.state.set('unknown');
      this.failed.set(true);
      this.message.set('The cancellation response could not be confirmed.');
      if (origin) this.focusAfterRender(() => this.heading()?.nativeElement, owner, context, origin);
    } finally {
      if (this.current(owner, context, request)) this.busy.set(false);
    }
  }

  async checkStatus(): Promise<void> {
    const intent = this.intent();
    const task = this.task();
    if (!intent || this.state() !== 'unknown' || this.busy()) return;
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
      const row = this.exactObservation(workspace, task.taskId, task.engagementId, intent.expectedRevision);
      if (row.status === 'CANCELLED') {
        if (row.revision <= intent.expectedRevision)
          throw new Error('Cancellation status did not advance the reviewed revision. Keep the outcome unresolved and refresh before relying on it.');
        this.state.set('observed');
      }
      else if (row.revision === intent.expectedRevision && ['OPEN', 'IN_PROGRESS'].includes(row.status)) {
        this.state.set('retryable');
        this.assent.set(false);
      } else this.state.set('stale');
      this.observed.set(row);
      if (origin) this.focusAfterRender(() => this.heading()?.nativeElement, owner, context, origin);
    } catch (error) {
      if (!this.current(owner, context, request)) return;
      this.failed.set(true);
      this.message.set(error instanceof Error ? error.message : 'The exact current task could not be verified. No cancellation was resent.');
      if (origin) this.focusAfterRender(() => this.heading()?.nativeElement, owner, context, origin);
    } finally {
      if (this.current(owner, context, request)) this.busy.set(false);
    }
  }

  acknowledgeAndReviewActivity(): void {
    if (this.state() !== 'observed' || !this.observed()) return;
    this.reviewActivity.emit();
  }

  acknowledgeChangedTask(): void {
    if (this.state() !== 'stale' || !this.observed()) return;
    this.refreshRequested.emit();
    this.closeRequested.emit();
  }

  private exactObservation(workspace: Workspace, taskId: string, engagementId: string, expectedRevision: number): Workspace['tasks'][number] {
    if (workspace.engagementId !== engagementId) throw new Error('The task workspace did not match the reviewed engagement. Keep the original cancellation unresolved.');
    const matches = workspace.tasks.filter(task => task.id === taskId);
    if (matches.length !== 1 || matches[0].revision < expectedRevision)
      throw new Error('The exact current task could not be verified. Keep the original cancellation unresolved; no request was resent.');
    return matches[0];
  }

  private reset(): void {
    ++this.request;
    this.state.set('editing');
    this.intent.set(null);
    this.observed.set(null);
    this.assent.set(false);
    this.busy.set(false);
    this.failed.set(false);
    this.message.set('');
    this.reason = '';
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
      task.engagementPeriod, task.revision, task.taskStatus, task.canCancel].join('|');
  }

  private cancelUrl(task: WorkflowTaskCancellationTarget): string {
    return `/api/ui/engagements/${task.engagementId}/workflow/tasks/${task.taskId}/cancel`;
  }

  private workspaceUrl(task: WorkflowTaskCancellationTarget): string {
    return `/api/ui/engagements/${task.engagementId}/workflow/tasks?taskId=${encodeURIComponent(task.taskId)}`;
  }
}
