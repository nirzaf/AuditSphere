import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, date, decode, guid, instant, int, nullable, obj, str } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';

const publishedRevision = obj({
  revisionId: guid, version: int, name: str(160), serviceRoute: str(50),
  industryKey: nullable(str(80)), contentDigest: str(64),
});
const templateItem = obj({
  templateId: guid, latestRevisionId: nullable(guid), version: nullable(int), name: nullable(str(160)),
  serviceRoute: nullable(str(50)), industryKey: nullable(str(80)), revisionStatus: nullable(str(32)),
  archived: bool, createdAt: instant, currentPublishedRevision: nullable(publishedRevision),
});
const templateCatalog = arr(templateItem, 100);
const ownerCandidate = obj({ userId: guid, displayName: str(200), staffingLevel: str(80) });
const planStep = obj({
  stepKey: str(80), templateStepId: guid, title: str(200), instructions: str(4000), phase: str(40),
  sortOrder: int, dueDate: nullable(date), plannedEffortMinutes: nullable(int),
  suggestedStaffingLevel: nullable(str(80)), selectedAssigneeUserId: nullable(guid),
  eligibleOwners: arr(ownerCandidate, 100), dependsOnStepKeys: arr(str(80), 100),
  missingEligibleAssignee: bool, existingTaskId: nullable(guid),
});
const planPreview = obj({
  engagementId: guid, engagementGeneration: int, templateId: guid, templateRevisionId: guid,
  templateVersion: int, templateName: str(160), templateContentDigest: str(64),
  milestonePlanId: nullable(guid), milestonePlanRevision: nullable(int), scheduleBasisDate: nullable(date),
  previewDigest: str(64), alreadyInstantiated: bool, existingTaskCount: int,
  templateUpdateNotice: nullable(str(300)), steps: arr(planStep, 100), canExecute: bool,
});
const planReceipt = obj({
  receiptId: guid, engagementId: guid, templateId: guid, templateRevisionId: guid,
  templateVersion: int, requestDigest: str(64), taskCount: int, replayed: bool,
  createdAt: instant, taskIds: arr(guid, 100),
});

type TemplateItem = ReturnType<typeof templateItem>;
type PlanPreview = ReturnType<typeof planPreview>;
type PlanReceipt = ReturnType<typeof planReceipt>;
type PendingPlan = { templateRevisionId: string; previewDigest: string; idempotencyKey: string };
type SelectableTemplate = { templateId: string; revision: NonNullable<TemplateItem['currentPublishedRevision']> };

function isPendingPlan(value: unknown): value is PendingPlan {
  if (!value || typeof value !== 'object') return false;
  const pending = value as Record<string, unknown>;
  return typeof pending['templateRevisionId'] === 'string' && /^[0-9a-f-]{36}$/i.test(pending['templateRevisionId']) &&
    typeof pending['idempotencyKey'] === 'string' && /^[0-9a-f-]{36}$/i.test(pending['idempotencyKey']) &&
    typeof pending['previewDigest'] === 'string' && /^[0-9a-f]{64}$/.test(pending['previewDigest']);
}

@Component({
  selector: 'audit-engagement-task-plan',
  imports: [MatButtonModule, ...SHARED],
  template: `
    <section class="task-plan" aria-labelledby="task-plan-heading">
      <h2 id="task-plan-heading">Operational task plan</h2>
      <p>Preview a published firm template against this engagement’s staffing and schedule. Created tasks organize work; they do not satisfy audit, review, approval, reporting, or archive gates.</p>
      <audit-state [loading]="catalog.loading()" [error]="catalog.error()" label="published workflow templates" />
      @if (catalog.data(); as items) {
        <fieldset [disabled]="busy() || pending() !== null || corruptPending()">
          <legend>Published templates for {{ serviceRoute() }}</legend>
          @for (item of publishedTemplates(); track item.revision.revisionId) {
            <label class="template-choice">
              <input type="radio" name="taskPlanTemplate" [value]="item.revision.revisionId"
                [checked]="selectedRevisionId() === item.revision.revisionId"
                (change)="selectTemplate(item.revision.revisionId)" />
              <span><strong>{{ item.revision.name }}</strong><small>v{{ item.revision.version }} · {{ item.revision.serviceRoute }} · {{ item.revision.contentDigest }}</small></span>
            </label>
          } @empty {
            <p>No current published template matches this engagement’s service route. A draft or superseded revision cannot be instantiated.</p>
          }
        </fieldset>
        <div class="actions">
          <button matButton="filled" (click)="previewPlan()" [disabled]="!canPreview()">Preview task plan</button>
        </div>
      }

      @if (corruptPending()) {
        <p role="alert">A saved plan-recovery marker is unreadable. Do not create another plan; contact a firm administrator to reconcile this engagement.</p>
      }
      @if (pending(); as pendingPlan) {
        <section class="recovery" aria-labelledby="plan-recovery-heading">
          <h3 id="plan-recovery-heading">Plan execution needs reconciliation</h3>
          <p>The execute response was not confirmed. Keep this idempotency key and recover its receipt before attempting any new plan.</p>
          <code>{{ pendingPlan.idempotencyKey }}</code>
          <button matButton (click)="recoverReceipt()" [disabled]="busy()">Recover saved receipt</button>
        </section>
      }

      @if (plan(); as preview) {
        <section class="preview" aria-labelledby="plan-preview-heading">
          <h3 id="plan-preview-heading">{{ preview.templateName }} · v{{ preview.templateVersion }}</h3>
          <dl>
            <dt>Template content digest</dt><dd><code>{{ preview.templateContentDigest }}</code></dd>
            <dt>Engagement generation</dt><dd>{{ preview.engagementGeneration }}</dd>
            <dt>Schedule basis</dt><dd>{{ preview.scheduleBasisDate ?? 'Manual dates only' }}@if (preview.milestonePlanRevision !== null) { · milestone revision {{ preview.milestonePlanRevision }} }</dd>
            <dt>Preview digest</dt><dd><code>{{ preview.previewDigest }}</code></dd>
          </dl>
          @if (preview.templateUpdateNotice) { <p role="status">{{ preview.templateUpdateNotice }}</p> }
          @if (preview.alreadyInstantiated) {
            <p role="status">This template revision already has a plan on this engagement. Use the existing tasks; duplicate instantiation is blocked.</p>
          }
          @if (preview.existingTaskCount > 0 && !preview.alreadyInstantiated) {
            <p role="status">{{ preview.existingTaskCount }} existing engagement task(s) were considered. Review them before proceeding.</p>
          }
          <ol class="plan-steps">
            @for (step of preview.steps; track step.stepKey) {
              <li>
                <h4>{{ step.sortOrder }}. {{ step.title }}</h4>
                <p>{{ step.instructions }}</p>
                <p><audit-status [value]="step.phase" /> · {{ step.dueDate ?? 'No due date' }} · {{ step.plannedEffortMinutes ?? 'No effort estimate' }} min</p>
                @if (step.dependsOnStepKeys.length) { <p>Prerequisites: {{ step.dependsOnStepKeys.join(', ') }}</p> }
                @if (step.eligibleOwners.length) {
                  <label>Assigned engagement staff
                    <select [value]="step.selectedAssigneeUserId ?? ''" (change)="selectOwner(step.stepKey, $any($event.target).value)"
                      [disabled]="busy() || pending() !== null || corruptPending()">
                      <option value="">Leave unassigned</option>
                      @for (owner of step.eligibleOwners; track owner.userId) {
                        <option [value]="owner.userId">{{ owner.displayName }} · {{ owner.staffingLevel }}</option>
                      }
                    </select>
                  </label>
                } @else if (step.missingEligibleAssignee) {
                  <p role="status">No active engagement assignee matches the suggested staffing level. The task will remain unassigned for authorized routing.</p>
                } @else {
                  <p>No eligible engagement staff is currently assigned; the task will remain unassigned.</p>
                }
                @if (step.existingTaskId) { <p>Existing task <code>{{ step.existingTaskId }}</code></p> }
              </li>
            }
          </ol>
          @if (!preview.canExecute && !preview.alreadyInstantiated) {
            <p role="status">This account can preview the plan but cannot instantiate it. A Partner, Manager, or Administrator must review and execute it.</p>
          }
          @if (preview.canExecute && !preview.alreadyInstantiated) {
            <label class="review-check"><input type="checkbox" [checked]="reviewed()" (change)="reviewed.set($any($event.target).checked)" /> I reviewed this exact plan, staffing, dates, and template revision.</label>
            <button matButton="filled" (click)="executePlan()" [disabled]="!canExecute()">Create operational tasks</button>
          }
        </section>
      }

      @if (receipt(); as completed) {
        <section class="receipt" role="status" aria-labelledby="plan-receipt-heading">
          <h3 id="plan-receipt-heading">Plan receipt confirmed</h3>
          <p>{{ completed.taskCount }} operational task(s) created from template v{{ completed.templateVersion }} · {{ completed.createdAt }}</p>
          <p>Receipt <code>{{ completed.receiptId }}</code>@if (completed.replayed) { · recovered from the original request }</p>
          <ul>@for (taskId of completed.taskIds; track taskId) { <li><code>{{ taskId }}</code></li> }</ul>
          <p>These tasks do not change professional approval, engagement lifecycle, or report-release decisions.</p>
        </section>
      }
      @if (message()) { <p [attr.role]="failed() ? 'alert' : 'status'" [class.error-text]="failed()">{{ message() }}</p> }
    </section>
  `,
  styleUrl: './engagement-task-plan.scss',
})
export class EngagementTaskPlan {
  readonly engagementId = input.required<string>();
  readonly serviceRoute = input.required<string>();
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  readonly catalog = this.api.resource(() => '/api/ui/practice/workflow-templates', templateCatalog,
    'Workflow task planning is unavailable or is not enabled on this server.');
  readonly publishedTemplates = computed<SelectableTemplate[]>(() => (this.catalog.data() ?? [])
    .filter((item) => !item.archived && item.currentPublishedRevision?.serviceRoute === this.serviceRoute())
    .map((item) => ({ templateId: item.templateId, revision: item.currentPublishedRevision! })));
  readonly selectedRevisionId = signal('');
  readonly plan = signal<PlanPreview | null>(null);
  readonly receipt = signal<PlanReceipt | null>(null);
  readonly pending = signal<PendingPlan | null>(null);
  readonly corruptPending = signal(false);
  readonly ownerSelections = signal<Record<string, string>>({});
  readonly reviewed = signal(false);
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly failed = signal(false);
  readonly message = signal('');
  readonly canPreview = computed(() => this.publishedTemplates().some((item) => item.revision.revisionId === this.selectedRevisionId()) && !this.busy() &&
    this.pending() === null && !this.corruptPending());
  readonly canExecute = computed(() => {
    const preview = this.plan();
    return !!preview?.canExecute && !preview.alreadyInstantiated && this.reviewed() &&
      !this.busy() && this.pending() === null && !this.corruptPending();
  });

  constructor() {
    effect(() => {
      const engagementId = this.engagementId();
      const session = this.session.current();
      this.session.invalidation();
      untracked(() => {
        this.selectedRevisionId.set('');
        this.plan.set(null);
        this.receipt.set(null);
        this.ownerSelections.set({});
        this.reviewed.set(false);
        this.uncertain.set(false);
        this.message.set('');
        this.restorePending(engagementId, session);
      });
    });
  }

  selectTemplate(revisionId: string): void {
    if (!this.publishedTemplates().some((item) => item.revision.revisionId === revisionId)) return;
    this.selectedRevisionId.set(revisionId);
    this.plan.set(null);
    this.receipt.set(null);
    this.ownerSelections.set({});
    this.reviewed.set(false);
    this.message.set('');
  }

  selectOwner(stepKey: string, userId: string): void {
    const current = { ...this.ownerSelections() };
    if (userId) current[stepKey] = userId;
    else delete current[stepKey];
    this.ownerSelections.set(current);
    this.plan.set(null);
    this.reviewed.set(false);
    this.message.set('Owner selection changed. Preview the plan again to review the updated digest.');
  }

  async previewPlan(): Promise<void> {
    if (!this.canPreview()) return;
    this.plan.set(null);
    this.reviewed.set(false);
    this.busy.set(true);
    this.failed.set(false);
    this.message.set('');
    try {
      const result = await this.api.command<unknown>(this.planUrl('preview'), {
        templateRevisionId: this.selectedRevisionId(), owners: this.ownerPayload(),
      });
      if (!result.ok) {
        this.failed.set(true);
        this.message.set(result.unknown ? 'The preview was not confirmed. Preview again; no task creation was requested.' : result.message);
        return;
      }
      const preview = decode(planPreview, result.value);
      if (preview.engagementId !== this.engagementId() || preview.templateRevisionId !== this.selectedRevisionId()) {
        this.failed.set(true);
        this.message.set('The server returned a plan for a different engagement or template. Refresh before continuing.');
        return;
      }
      this.plan.set(preview);
      this.reviewed.set(false);
      this.message.set('Current plan preview loaded. Review the exact dates, staffing, and digest before creating tasks.');
    } catch {
      this.failed.set(true);
      this.message.set('The preview response could not be verified. Refresh the engagement and preview again.');
    } finally {
      this.busy.set(false);
    }
  }

  async executePlan(): Promise<void> {
    const preview = this.plan();
    if (!preview || !this.canExecute()) return;
    const pendingPlan: PendingPlan = {
      templateRevisionId: preview.templateRevisionId,
      previewDigest: preview.previewDigest,
      idempotencyKey: crypto.randomUUID(),
    };
    if (!this.persistPending(pendingPlan)) {
      this.failed.set(true);
      this.message.set('A recovery key could not be saved in this browser, so no task plan was submitted.');
      return;
    }
    this.pending.set(pendingPlan);
    this.busy.set(true);
    this.failed.set(false);
    this.message.set('');
    try {
      const result = await this.api.command<unknown>(this.planUrl('execute'), {
        templateRevisionId: pendingPlan.templateRevisionId,
        expectedPreviewDigest: pendingPlan.previewDigest,
        idempotencyKey: pendingPlan.idempotencyKey,
        owners: this.ownerPayload(),
      });
      if (!result.ok) {
        this.failed.set(true);
        if (result.unknown) {
          this.uncertain.set(true);
          this.message.set('The execute response was not confirmed. Recover the saved receipt before any new attempt.');
          return;
        }
        this.clearPending();
        this.message.set(result.message);
        return;
      }
      const confirmed = this.verifyReceipt(result.value, pendingPlan);
      if (!confirmed) {
        this.uncertain.set(true);
        this.failed.set(true);
        this.message.set('The execute response could not be matched to the saved request. Recover its receipt; do not submit another plan.');
        return;
      }
      this.acceptReceipt(confirmed);
    } catch {
      this.uncertain.set(true);
      this.failed.set(true);
      this.message.set('The execute outcome could not be verified. Recover the saved receipt before any new attempt.');
    } finally {
      this.busy.set(false);
    }
  }

  async recoverReceipt(): Promise<void> {
    const pending = this.pending();
    if (!pending || this.busy()) return;
    this.busy.set(true);
    this.failed.set(false);
    this.message.set('');
    try {
      const raw = await this.api.get(`/api/ui/engagements/${this.engagementId()}/task-plan/receipts/${pending.idempotencyKey}`, planReceipt);
      const confirmed = this.verifyReceipt(raw, pending);
      if (!confirmed) {
        this.failed.set(true);
        this.uncertain.set(true);
        this.message.set('The receipt did not match the saved request. Keep the recovery key and contact a firm administrator.');
        return;
      }
      this.acceptReceipt(confirmed);
    } catch {
      this.failed.set(true);
      this.uncertain.set(true);
      this.message.set('No receipt was confirmed. Keep this recovery key and retry receipt recovery; do not create another plan.');
    } finally {
      this.busy.set(false);
    }
  }

  private verifyReceipt(raw: unknown, pending: PendingPlan): PlanReceipt | null {
    try {
      const receipt = decode(planReceipt, raw);
      return receipt.engagementId === this.engagementId() &&
        receipt.templateRevisionId === pending.templateRevisionId &&
        receipt.requestDigest === pending.previewDigest ? receipt : null;
    } catch { return null; }
  }

  private acceptReceipt(receipt: PlanReceipt): void {
    this.receipt.set(receipt);
    this.clearPending();
    this.plan.set(null);
    this.uncertain.set(false);
    this.failed.set(false);
    this.message.set('The task plan receipt is confirmed.');
  }

  private ownerPayload(): Array<{ stepKey: string; assigneeUserId: string }> {
    return Object.entries(this.ownerSelections()).map(([stepKey, assigneeUserId]) => ({ stepKey, assigneeUserId }));
  }

  private planUrl(action: string): string {
    return `/api/ui/engagements/${this.engagementId()}/task-plan/${action}`;
  }

  private storageKey(engagementId: string, session: ReturnType<SessionService['current']>): string | null {
    if (!session?.staff || !engagementId) return null;
    return `auditsphere:workflow-plan:${session.firmId}:${session.userId}:${session.generation}:${engagementId}`;
  }

  private restorePending(engagementId: string, session: ReturnType<SessionService['current']>): void {
    this.pending.set(null);
    this.corruptPending.set(false);
    const key = this.storageKey(engagementId, session);
    if (!key) return;
    try {
      const raw = sessionStorage.getItem(key);
      if (!raw) return;
      const parsed: unknown = JSON.parse(raw);
      if (!isPendingPlan(parsed)) { this.corruptPending.set(true); return; }
      this.pending.set(parsed);
      this.uncertain.set(true);
    } catch { this.corruptPending.set(true); }
  }

  private persistPending(pending: PendingPlan): boolean {
    const key = this.storageKey(this.engagementId(), this.session.current());
    if (!key) return false;
    try { sessionStorage.setItem(key, JSON.stringify(pending)); return true; }
    catch { return false; }
  }

  private clearPending(): void {
    const key = this.storageKey(this.engagementId(), this.session.current());
    if (key) {
      try { sessionStorage.removeItem(key); } catch { /* the in-memory receipt remains visible */ }
    }
    this.pending.set(null);
    this.corruptPending.set(false);
  }
}
