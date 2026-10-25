import { Component, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, bool, date, decode, guid, instant, int, nullable, obj, oneOf, str } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { WorkflowEvidenceSourcePicker } from '../practice/workflow-evidence-source-picker';

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
const pbcCandidate = obj({ userId: guid, displayName: str(200) });
const pbcDefinition = obj({ objective: str(500), entityScope: str(500), area: str(120), requestedFormat: str(120),
  controlTotals: nullable(str(1000)), confidentiality: str(80), acceptanceCriteria: str(2000) });
const pbcPreview = obj({ state: str(40), reference: nullable(str(200)), definition: nullable(pbcDefinition),
  periodStart: date, periodEnd: date, dueDate: nullable(date), clientOwnerUserId: nullable(guid), firmOwnerUserId: nullable(guid),
  reviewerUserId: nullable(guid), pbcRequestId: nullable(guid), pbcRequestState: nullable(str(40)) });
const evidencePreview = obj({ requirementKey: str(80), kind: oneOf('CURRENT_MATERIALITY_APPROVED', 'PBC_ACCEPTED_OR_CLOSED', 'CURRENT_PROCEDURE_REVIEWED'), sourceId: nullable(guid),
  bindingRequired: bool, satisfied: bool, sourceState: str(80), message: str(1000),
  sourceRevision: nullable(int), inputGeneration: nullable(int) });
const optionalEvidence = (value: unknown, path: string) => {
  const requirements = value === undefined || value === null ? [] : arr(evidencePreview, 8)(value, path);
  const keys = new Set<string>();
  for (const requirement of requirements) {
    if (!requirement.requirementKey.trim() || keys.has(requirement.requirementKey) ||
        requirement.sourceRevision !== null && requirement.sourceRevision < 1 ||
        requirement.inputGeneration !== null && requirement.inputGeneration < 1)
      throw new Error('Invalid template evidence reference.');
    keys.add(requirement.requirementKey);
    if (requirement.satisfied && (requirement.sourceId === null || !(requirement.kind === 'CURRENT_MATERIALITY_APPROVED'
      ? requirement.sourceState === 'APPROVED' : requirement.kind === 'PBC_ACCEPTED_OR_CLOSED'
      ? ['ACCEPTED', 'CLOSED'].includes(requirement.sourceState) : requirement.sourceState === 'REVIEWED_CURRENT')))
      throw new Error('Evidence state does not support satisfaction.');
  }
  return requirements;
};
const planStep = obj({
  stepKey: str(80), templateStepId: guid, title: str(200), instructions: str(4000), phase: str(40),
  sortOrder: int, dueDate: nullable(date), plannedEffortMinutes: nullable(int),
  suggestedStaffingLevel: nullable(str(80)), selectedAssigneeUserId: nullable(guid),
  eligibleOwners: arr(ownerCandidate, 100), dependsOnStepKeys: arr(str(80), 100),
  evidenceRequirements: optionalEvidence, missingEligibleAssignee: bool, existingTaskId: nullable(guid), pbc: nullable(pbcPreview),
});
const planPreview = obj({
  engagementId: guid, engagementGeneration: int, templateId: guid, templateRevisionId: guid,
  templateVersion: int, templateName: str(160), templateContentDigest: str(64),
  milestonePlanId: nullable(guid), milestonePlanRevision: nullable(int), scheduleBasisDate: nullable(date),
  previewDigest: str(64), alreadyInstantiated: bool, existingTaskCount: int,
  templateUpdateNotice: nullable(str(300)), steps: arr(planStep, 100), engagementStatus: str(32),
  pbcClientOwners: arr(pbcCandidate, 100), pbcFirmOwners: arr(pbcCandidate, 100), pbcReviewers: arr(pbcCandidate, 100),
  canExecute: bool, canCreatePbcDrafts: bool,
});
const planReceipt = obj({
  receiptId: guid, engagementId: guid, templateId: guid, templateRevisionId: guid,
  templateVersion: int, requestDigest: str(64), taskCount: int, replayed: bool,
  createdAt: instant, taskIds: arr(guid, 100),
  pbcDrafts: arr(obj({ stepKey: str(80), templateStepId: guid, taskId: guid, pbcRequestId: guid }), 100),
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
  imports: [MatButtonModule, WorkflowEvidenceSourcePicker, ...SHARED],
  template: `
    <section class="task-plan" aria-labelledby="task-plan-heading">
      <h2 id="task-plan-heading">Operational task plan</h2>
      <p>Preview a published firm template against this engagement’s staffing and schedule. Created tasks organize work; they do not satisfy audit, review, approval, reporting, or archive gates.</p>
      @if (suggestedRevisionId()) {
        <p>The annual preparation selected revision {{ suggestedRevisionId() }} as a reference. Review the current staffing, dates and exact template before creating tasks.</p>
        @if (!catalog.loading() && !catalog.error() && referenceUnavailable()) {
          <p role="alert">The referenced revision is unavailable or is no longer current. Choose and review a current published template; no replacement has been selected automatically.</p>
        }
      }
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
          <button #previewTaskPlan matButton="filled" (click)="previewPlan()" [disabled]="!canPreview()">Preview task plan</button>
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
          @if (planSummary(preview.steps); as summary) {
            <p class="plan-summary">{{ summary.steps }} step(s) · {{ summary.unassigned }} without an eligible engagement assignee ·
              {{ summary.undated }} without a due date · {{ summary.withPrerequisites }} with prerequisites</p>
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
                @if (step.evidenceRequirements.length) {
                  <section aria-label="Authoritative evidence prerequisites">
                    <h5>Evidence prerequisites</h5>
                    <p>Plan creation binds sources only. Existing source workflows retain approval and review authority.</p>
                    @for (requirement of step.evidenceRequirements; track requirement.requirementKey) {
                      <p><strong>{{ requirement.requirementKey }}</strong> · {{ requirement.kind }} · {{ requirement.sourceState }}</p>
                      <p [attr.role]="requirement.bindingRequired ? 'status' : null">{{ requirement.message }}</p>
                      @if ((requirement.kind === 'PBC_ACCEPTED_OR_CLOSED' || requirement.kind === 'CURRENT_PROCEDURE_REVIEWED') && !preview.alreadyInstantiated) {
                        <audit-workflow-evidence-source-picker
                          [engagementId]="engagementId()"
                          [kind]="requirement.kind"
                          [stepTitle]="'Step ' + step.sortOrder + ': ' + step.title"
                          [requirementKey]="requirement.requirementKey"
                          [pickerId]="evidencePickerId(engagementId(), step.templateStepId, requirement.requirementKey)"
                          [selectedId]="evidenceValue(step.stepKey, requirement.requirementKey, requirement.sourceId)"
                          [disabled]="busy() || pending() !== null || corruptPending()"
                          (sourceSelected)="selectEvidence(step.stepKey, requirement.requirementKey, $event)" />
                      }
                      @if (requirement.kind !== 'CURRENT_MATERIALITY_APPROVED' && !preview.alreadyInstantiated) {
                        <label>Exact source reference for {{ requirement.requirementKey }}
                          <input [value]="evidenceValue(step.stepKey, requirement.requirementKey, requirement.sourceId)"
                            (change)="selectEvidence(step.stepKey, requirement.requirementKey, $any($event.target).value)"
                            maxlength="36" [attr.aria-describedby]="'plan-evidence-help-' + evidencePickerId(engagementId(), step.templateStepId, requirement.requirementKey)"
                            [disabled]="busy() || pending() !== null || corruptPending()" />
                        </label>
                        <small [id]="'plan-evidence-help-' + evidencePickerId(engagementId(), step.templateStepId, requirement.requirementKey)">Use the exact PBC request or procedure identifier from this engagement’s authorized workspace, then preview again.</small>
                      } @else if (requirement.sourceId) { <p>Source <code>{{ requirement.sourceId }}</code></p> }
                    }
                  </section>
                }
                @if (step.existingTaskId) { <p>Existing task <code>{{ step.existingTaskId }}</code></p> }
                @if (step.pbc; as pbc) {
                  <section class="pbc-plan" aria-label="PBC request plan">
                    <h5>Evidence request · {{ pbc.state }}</h5>
                    @if (pbc.definition; as definition) {
                      <p>{{ definition.objective }} · {{ definition.entityScope }} · {{ definition.area }}</p>
                      <p>{{ definition.requestedFormat }} · {{ pbc.periodStart }} to {{ pbc.periodEnd }} · due {{ pbc.dueDate ?? 'No due date' }}</p>
                      <p>Confidentiality: {{ definition.confidentiality }} · Acceptance: {{ definition.acceptanceCriteria }}</p>
                      @if (pbc.state === 'OWNERS_REQUIRED' || pbc.state === 'READY_DRAFT') {
                        <div class="pbc-owners">
                          <label>Client owner<select [value]="pbcOwnerValue(step.stepKey, 'clientOwnerUserId', pbc.clientOwnerUserId)" (change)="selectPbcOwner(step.stepKey, 'clientOwnerUserId', $any($event.target).value)"><option value="">Choose client owner</option>@for (owner of preview.pbcClientOwners; track owner.userId) { <option [value]="owner.userId">{{ owner.displayName }}</option> }</select></label>
                          <label>Firm owner<select [value]="pbcOwnerValue(step.stepKey, 'firmOwnerUserId', pbc.firmOwnerUserId)" (change)="selectPbcOwner(step.stepKey, 'firmOwnerUserId', $any($event.target).value)"><option value="">Choose firm owner</option>@for (owner of preview.pbcFirmOwners; track owner.userId) { <option [value]="owner.userId">{{ owner.displayName }}</option> }</select></label>
                          <label>Independent reviewer<select [value]="pbcOwnerValue(step.stepKey, 'reviewerUserId', pbc.reviewerUserId)" (change)="selectPbcOwner(step.stepKey, 'reviewerUserId', $any($event.target).value)"><option value="">Choose reviewer</option>@for (owner of preview.pbcReviewers; track owner.userId) { <option [value]="owner.userId">{{ owner.displayName }}</option> }</select></label>
                        </div>
                      }
                      @if (pbc.pbcRequestId) { <p>Linked request <code>{{ pbc.pbcRequestId }}</code> · {{ pbc.pbcRequestState }}</p> }
                      @if (pbc.state === 'BLOCKED_ACTIVATION') { <p role="status">PBC draft creation is deferred until the engagement is active.</p> }
                    } @else { <p role="status">Legacy reference {{ pbc.reference }} is descriptive only and will not create a request.</p> }
                  </section>
                }
              </li>
            }
          </ol>
          @if (!preview.canExecute && !preview.alreadyInstantiated) {
            <p role="status">Plan creation is blocked. Review any missing evidence source bindings above and the account’s current permissions. Only an authorized Partner, Manager, or Administrator may execute a complete plan.</p>
          }
          @if (preview.canExecute && !preview.alreadyInstantiated) {
            <label class="review-check"><input type="checkbox" [checked]="reviewed()" (change)="reviewed.set($any($event.target).checked)" /> I reviewed this exact plan, staffing, dates, and template revision.</label>
            <button matButton="filled" (click)="executePlan()" [disabled]="!canExecute()">Create operational tasks</button>
          }
          @if (preview.canCreatePbcDrafts && preview.alreadyInstantiated) {
            <label class="review-check"><input type="checkbox" [checked]="reviewed()" (change)="reviewed.set($any($event.target).checked)" /> I reviewed these exact PBC definitions, owners, period, and due dates.</label>
            <button matButton="filled" (click)="createPbcDrafts()" [disabled]="!reviewed() || busy()">Create unsent PBC drafts</button>
          }
        </section>
      }

      @if (receipt(); as completed) {
        <section class="receipt" role="status" aria-labelledby="plan-receipt-heading">
          <h3 id="plan-receipt-heading">Plan receipt confirmed</h3>
          <p>{{ completed.taskCount }} operational task(s) created from template v{{ completed.templateVersion }} · {{ completed.createdAt }}</p>
          <p>Receipt <code>{{ completed.receiptId }}</code>@if (completed.replayed) { · recovered from the original request }</p>
          <ul>@for (taskId of completed.taskIds; track taskId) { <li><code>{{ taskId }}</code></li> }</ul>
          @if (completed.pbcDrafts.length) { <h4>Unsent PBC drafts</h4><ul>@for (draft of completed.pbcDrafts; track draft.pbcRequestId) { <li>{{ draft.stepKey }} · task <code>{{ draft.taskId }}</code> · request <code>{{ draft.pbcRequestId }}</code></li> }</ul> }
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
  readonly suggestedRevisionId = input<string | null>(null);
  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly previewTaskPlan = viewChild<unknown, ElementRef<HTMLButtonElement>>('previewTaskPlan', { read: ElementRef });
  readonly catalog = this.api.resource(() => `/api/ui/engagements/${this.engagementId()}/task-plan/templates`, templateCatalog,
    'Workflow task planning is unavailable or is not enabled on this server.');
  readonly publishedTemplates = computed<SelectableTemplate[]>(() => (this.catalog.data() ?? [])
    .filter((item) => !item.archived && item.currentPublishedRevision?.serviceRoute === this.serviceRoute())
    .map((item) => ({ templateId: item.templateId, revision: item.currentPublishedRevision! })));
  readonly selectedRevisionId = signal('');
  /** Counts over the previewed steps only; it states gaps and changes nothing in the reviewed plan. */
  planSummary(steps: PlanPreview['steps']) {
    return { steps: steps.length, unassigned: steps.filter(step => step.missingEligibleAssignee).length,
      undated: steps.filter(step => step.dueDate === null).length, withPrerequisites: steps.filter(step => step.dependsOnStepKeys.length > 0).length };
  }
  readonly referenceUnavailable = computed(() => !!this.suggestedRevisionId() &&
    !this.publishedTemplates().some(item => item.revision.revisionId === this.suggestedRevisionId()));
  readonly plan = signal<PlanPreview | null>(null);
  readonly receipt = signal<PlanReceipt | null>(null);
  readonly pending = signal<PendingPlan | null>(null);
  readonly corruptPending = signal(false);
  readonly evidenceSelections = signal<Array<{ stepKey: string; requirementKey: string; sourceId: string }>>([]);
  readonly ownerSelections = signal<Record<string, string>>({});
  readonly pbcOwnerSelections = signal<Record<string, { clientOwnerUserId: string; firmOwnerUserId: string; reviewerUserId: string }>>({});
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
      this.serviceRoute();
      this.suggestedRevisionId();
      const session = this.session.current();
      this.session.invalidation();
      untracked(() => {
        this.selectedRevisionId.set('');
        this.plan.set(null);
        this.receipt.set(null);
        this.ownerSelections.set({});
        this.evidenceSelections.set([]);
        this.pbcOwnerSelections.set({});
        this.reviewed.set(false);
        this.uncertain.set(false);
        this.message.set('');
        this.restorePending(engagementId, session);
      });
    });
    effect(() => {
      const choices = this.publishedTemplates();
      const suggested = this.suggestedRevisionId();
      untracked(() => {
        if (suggested && choices.some(item => item.revision.revisionId === suggested) &&
          !this.selectedRevisionId() && !this.pending() && !this.corruptPending() && !this.busy() && !this.plan() && !this.receipt())
          this.selectTemplate(suggested);
      });
    });
  }

  selectTemplate(revisionId: string): void {
    if (!this.publishedTemplates().some((item) => item.revision.revisionId === revisionId)) return;
    this.selectedRevisionId.set(revisionId);
    this.plan.set(null);
    this.receipt.set(null);
    this.ownerSelections.set({});
        this.evidenceSelections.set([]);
    this.pbcOwnerSelections.set({});
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

  evidenceValue(stepKey: string, requirementKey: string, fallback: string | null): string {
    return this.evidenceSelections().find(selection => selection.stepKey === stepKey &&
      selection.requirementKey === requirementKey)?.sourceId ?? fallback ?? '';
  }

  evidencePickerId(engagementId: string, templateStepId: string, requirementKey: string): string {
    const encodedKey = Array.from(requirementKey, character => character.codePointAt(0)!.toString(16)).join('-') || 'empty';
    return `workflow-evidence-${engagementId}-${templateStepId}-${encodedKey}`;
  }

  selectEvidence(stepKey: string, requirementKey: string, raw: string): void {
    if (this.busy() || this.pending() || this.corruptPending()) return;
    // Source changes remove the old preview. Keep keyboard focus on the next required action.
    this.previewTaskPlan()?.nativeElement.focus();
    const sourceId = raw.trim();
    if (sourceId && !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(sourceId)) {
      this.evidenceSelections.update(selections => selections.filter(selection =>
        selection.stepKey !== stepKey || selection.requirementKey !== requirementKey));
      this.failed.set(true); this.reviewed.set(false); this.plan.set(null);
      this.message.set('Enter a valid exact source identifier from this engagement.');
      return;
    }
    this.evidenceSelections.update(selections => [...selections.filter(selection => selection.stepKey !== stepKey ||
      selection.requirementKey !== requirementKey), ...(sourceId ? [{ stepKey, requirementKey, sourceId }] : [])]);
    this.plan.set(null); this.reviewed.set(false); this.failed.set(false);
    this.message.set('Evidence source changed. Preview again to verify scope and review the updated digest.');
  }

  selectPbcOwner(stepKey: string, role: 'clientOwnerUserId' | 'firmOwnerUserId' | 'reviewerUserId', userId: string): void {
    const current = { ...this.pbcOwnerSelections() };
    const selection = { ...(current[stepKey] ?? { clientOwnerUserId: '', firmOwnerUserId: '', reviewerUserId: '' }), [role]: userId };
    if (!selection.clientOwnerUserId && !selection.firmOwnerUserId && !selection.reviewerUserId) delete current[stepKey];
    else current[stepKey] = selection;
    this.pbcOwnerSelections.set(current);
    this.plan.set(null);
    this.reviewed.set(false);
    this.message.set('PBC owners changed. Preview again to review the updated digest.');
  }

  pbcOwnerValue(stepKey: string, role: 'clientOwnerUserId' | 'firmOwnerUserId' | 'reviewerUserId', fallback: string | null): string {
    return this.pbcOwnerSelections()[stepKey]?.[role] ?? fallback ?? '';
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
        templateRevisionId: this.selectedRevisionId(), owners: this.ownerPayload(), pbcOwners: this.pbcOwnerPayload(), evidenceSelections: this.evidenceSelections(),
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
        owners: this.ownerPayload(), pbcOwners: this.pbcOwnerPayload(), evidenceSelections: this.evidenceSelections(),
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

  private pbcOwnerPayload(): Array<{ stepKey: string; clientOwnerUserId: string; firmOwnerUserId: string; reviewerUserId: string }> {
    return Object.entries(this.pbcOwnerSelections()).filter(([, owners]) => owners.clientOwnerUserId && owners.firmOwnerUserId && owners.reviewerUserId)
      .map(([stepKey, owners]) => ({ stepKey, ...owners }));
  }

  async createPbcDrafts(): Promise<void> {
    const preview = this.plan();
    if (!preview?.canCreatePbcDrafts || !preview.alreadyInstantiated || !this.reviewed() || this.busy()) return;
    this.busy.set(true); this.failed.set(false); this.message.set('');
    try {
      const result = await this.api.command<unknown>(this.planUrl('pbc-drafts'), {
        templateRevisionId: preview.templateRevisionId, expectedPreviewDigest: preview.previewDigest,
        pbcOwners: this.pbcOwnerPayload(),
      });
      if (!result.ok) { this.failed.set(true); this.message.set(result.unknown ? 'The draft outcome was not confirmed. Refresh and recover the retained plan receipt before retrying.' : result.message); return; }
      const receipt = decode(planReceipt, result.value);
      if (receipt.engagementId !== this.engagementId() || receipt.templateRevisionId !== preview.templateRevisionId ||
          receipt.pbcDrafts.length === 0 || receipt.pbcDrafts.some(link => !preview.steps.some(step =>
            step.templateStepId === link.templateStepId && step.stepKey === link.stepKey))) {
        this.failed.set(true); this.message.set('The PBC draft response could not be matched to the reviewed plan. Refresh and recover the retained receipt.'); return;
      }
      this.receipt.set(receipt); this.plan.set(null);
      this.message.set(`${receipt.pbcDrafts.length} unsent PBC draft(s) are linked to the retained task plan.`);
    } catch { this.failed.set(true); this.message.set('The PBC draft outcome could not be verified. Refresh the engagement and recover its retained plan receipt.'); }
    finally { this.busy.set(false); }
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
