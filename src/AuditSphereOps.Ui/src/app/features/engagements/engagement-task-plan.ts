import { Component, DestroyRef, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
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
  taskGroupName: nullable(str(120)), requiredTask: nullable(bool), applicabilityCondition: nullable(str(500)),
  sortOrder: int, dueDate: nullable(date), plannedEffortMinutes: nullable(int),
  suggestedStaffingLevel: nullable(str(80)), selectedAssigneeUserId: nullable(guid),
  eligibleOwners: arr(ownerCandidate, 100), dependsOnStepKeys: arr(str(80), 100),
  evidenceRequirements: optionalEvidence, missingEligibleAssignee: bool, existingTaskId: nullable(guid), pbc: nullable(pbcPreview),
});
const uniquePlanSteps = (value: unknown, path: string) => {
  const steps = arr(planStep, 100)(value, path);
  const ids = new Set<string>();
  const keys = new Set<string>();
  for (const step of steps) {
    const id = step.templateStepId.toLowerCase();
    if (ids.has(id) || keys.has(step.stepKey)) throw new Error('Duplicate template step identity.');
    ids.add(id);
    keys.add(step.stepKey);
  }
  return steps;
};
const planPreview = obj({
  engagementId: guid, engagementGeneration: int, templateId: guid, templateRevisionId: guid,
  templateVersion: int, templateName: str(160), templateContentDigest: str(64),
  milestonePlanId: nullable(guid), milestonePlanRevision: nullable(int), scheduleBasisDate: nullable(date),
  previewDigest: str(64), alreadyInstantiated: bool, existingTaskCount: int,
  templateUpdateNotice: nullable(str(300)), steps: uniquePlanSteps, engagementStatus: str(32),
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
type PlanStep = PlanPreview['steps'][number];
type UnresolvedInput = { stepId: string; targetId: string; label: string };
type PbcOwners = { clientOwnerUserId: string; firmOwnerUserId: string; reviewerUserId: string };
type PbcRefusal = { context: PlanContext; revisionId: string; submitted: Array<PbcOwners & { stepKey: string }> };
type PlanReceipt = ReturnType<typeof planReceipt>;
type PendingPlan = { templateRevisionId: string; previewDigest: string; idempotencyKey: string };
type SelectableTemplate = { templateId: string; revision: NonNullable<TemplateItem['currentPublishedRevision']> };
type PlanContext = { engagementId: string; session: NonNullable<ReturnType<SessionService['current']>>; invalidation: number; version: number; accessEpoch: number; catalogError: string };
type SavedPlan = { key: string; raw: string; plan: PendingPlan; priorGeneration: boolean; storage: Storage };
type ReservationCleanup = { context: PlanContext; key: string; raw: string; storage: Storage; removalStarted: boolean };
const catalogUnavailable = 'Workflow task planning is unavailable or is not enabled on this server.';

function isPendingPlan(value: unknown): value is PendingPlan {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const pending = value as Record<string, unknown>;
  if (Object.keys(pending).some(key => !['templateRevisionId', 'previewDigest', 'idempotencyKey'].includes(key))) return false;
  const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  return typeof pending['templateRevisionId'] === 'string' && uuid.test(pending['templateRevisionId']) &&
    pending['templateRevisionId'] !== '00000000-0000-0000-0000-000000000000' &&
    typeof pending['idempotencyKey'] === 'string' && uuid.test(pending['idempotencyKey']) &&
    pending['idempotencyKey'] !== '00000000-0000-0000-0000-000000000000' &&
    typeof pending['previewDigest'] === 'string' && /^[0-9a-f]{64}$/.test(pending['previewDigest']);
}

@Component({
  selector: 'audit-engagement-task-plan',
  imports: [MatButtonModule, WorkflowEvidenceSourcePicker, ...SHARED],
  template: `
    <section class="task-plan" aria-labelledby="task-plan-heading">
      <h2 id="task-plan-heading">Operational task plan</h2>
      <p>Preview a published firm template against this engagement’s staffing and schedule. Created tasks organize work; they do not satisfy audit, review, approval, reporting, or archive gates.</p>
      <p class="hint">An engagement plan can be created only from a published template with at most 100 steps.</p>
      @if (!accessBlocked() && suggestedRevisionId()) {
        <p>The annual preparation selected revision {{ suggestedRevisionId() }} as a reference. Review the current staffing, dates and exact template before creating tasks.</p>
        @if (!catalog.loading() && !catalog.error() && referenceUnavailable()) {
          <p role="alert">The referenced revision is unavailable or is no longer current. Choose and review a current published template; no replacement has been selected automatically.</p>
        }
      }
      <audit-state [loading]="catalog.loading()" [error]="catalog.error()" label="published workflow templates" />
      @if (!accessBlocked() && catalog.data(); as items) {
        <fieldset [disabled]="busy() || pending() !== null || corruptPending() || priorRecovery()">
          <legend>Published templates for {{ serviceRoute() }}</legend>
          @if (publishedTemplates().length && !selectedRevisionId()) {
            <p role="status">Choose a published template to preview its tasks and inputs.</p>
          }
          @for (item of publishedTemplates(); track item.revision.revisionId) {
            <div>
              <label class="template-choice">
                <input type="radio" name="taskPlanTemplate" [value]="item.revision.revisionId"
                  [checked]="selectedRevisionId() === item.revision.revisionId"
                  (change)="selectTemplate(item.revision.revisionId)" />
                <span><strong>{{ item.revision.name }}</strong><small>v{{ item.revision.version }} · {{ item.revision.serviceRoute }}</small></span>
              </label>
              <details><summary>Template technical lineage for {{ item.revision.name }} v{{ item.revision.version }}</summary>
                <p>Revision <code>{{ item.revision.revisionId }}</code> · content digest <code>{{ item.revision.contentDigest }}</code></p>
              </details>
            </div>
          } @empty {
            <p>No current published template matches this engagement’s service route. A draft or superseded revision cannot be instantiated.</p>
          }
        </fieldset>
        <div class="actions">
          <button #previewTaskPlan matButton="filled" (click)="previewPlan()" [disabled]="!canPreview()">Preview task plan</button>
        </div>
      }
      @if (accessBlocked() && !priorRecovery() && !corruptPending()) {
        <p role="status">Task plan access needs to be checked again.</p>
        <button matButton (click)="refreshPlanAccess()" [disabled]="busy()">Check task plan access</button>
      }

      @if (corruptPending()) {
        @if (canCleanReservation()) {
          <p role="alert">A local reservation could not be verified. No plan was submitted in this attempt; retry its cleanup.</p>
          <button matButton (click)="retryReservationCleanup()" [disabled]="busy()">Retry cleanup of unsent reservation</button>
        } @else {
          <p role="alert">A saved plan-recovery marker is unreadable. Do not create another plan; contact a firm administrator to reconcile this engagement.</p>
        }
      }
      @if (priorRecovery() && !pending() && !corruptPending()) {
        <section class="recovery" aria-labelledby="plan-prior-recovery-heading">
          <h3 id="plan-prior-recovery-heading">A previous plan request needs reconciliation</h3>
          <p>Check your current access to this engagement before recovering the saved request.</p>
          <button matButton (click)="authorizePriorRecovery()" [disabled]="busy()">Check access to saved recovery</button>
        </section>
      }
      @if (pending(); as pendingPlan) {
        <section class="recovery" aria-labelledby="plan-recovery-heading">
          <h3 id="plan-recovery-heading">Plan execution needs reconciliation</h3>
          <p>The plan outcome remains unconfirmed. Keep this idempotency key and recover its receipt before attempting any new plan.</p>
          <code>{{ pendingPlan.idempotencyKey }}</code>
          <button matButton (click)="recoverReceipt()" [disabled]="busy()">Recover saved receipt</button>
        </section>
      }

      @if (currentPbcRefusal(); as refusal) {
        <section aria-label="PBC selection recovery">
          <p role="status">The preview could not accept a PBC owner. The response does not provide a structured step identity. Clear a step's local choices and preview again to review current suggestions.</p>
          @for (selection of refusal.submitted; track selection.stepKey) {
            <button type="button" matButton (click)="clearRefusedPbcSelection(selection.stepKey)"
              [disabled]="busy() || pending() !== null || corruptPending() || priorRecovery() || accessBlocked()">Clear local PBC choices for {{ selection.stepKey }}</button>
          }
        </section>
      }

      @if (plan(); as preview) {
        <section class="preview" aria-labelledby="plan-preview-heading">
          <h3 id="plan-preview-heading">{{ preview.templateName }} · v{{ preview.templateVersion }}</h3>
          <dl><dt>Proposed tasks</dt><dd>{{ preview.steps.length }}</dd>
            <dt>Schedule basis</dt><dd>{{ preview.scheduleBasisDate ?? 'Manual dates only' }}@if (preview.milestonePlanRevision !== null) { · milestone revision {{ preview.milestonePlanRevision }} }</dd>
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
          <section aria-labelledby="plan-unresolved-heading">
            <h4 id="plan-unresolved-heading">Inputs to review</h4>
            @if (unresolvedInputs().length) {
          <p>Review these items. An unassigned or undated task can be intentional; evidence approval remains separate.</p>
              <ul>
                @for (item of unresolvedInputs(); track item.targetId) {
                  <li><button type="button" matButton (click)="focusPlanInput(item.targetId)"
                    [disabled]="busy() || pending() !== null || corruptPending() || priorRecovery()">{{ item.label }}</button></li>
                }
              </ul>
            } @else { <p>No missing selections, undated tasks or pending evidence are shown in this preview.</p> }
          </section>
          @if (retainedPbcWithoutControls(preview); as retained) {
            @if (retained.length) {
              <section aria-label="Retained PBC choices">
                <p>These steps have retained local PBC choices, but the current preview has no owner controls for them. You can clear a step's choices and preview again.</p>
                @for (item of retained; track item.stepKey) {
                  <p>{{ item.label }}@if (item.mismatch) { · Choices differ from this preview; commands remain blocked. }</p>
                  <button type="button" matButton (click)="clearUnavailablePbcSelection(item.stepKey)"
                    [disabled]="busy() || pending() !== null || corruptPending() || priorRecovery() || accessBlocked()">Clear local PBC choices for {{ item.label }}</button>
                }
              </section>
            }
          }
          <details><summary>Technical plan lineage</summary>
            <dl>
              <dt>Template revision</dt><dd><code>{{ preview.templateRevisionId }}</code></dd>
              <dt>Template content digest</dt><dd><code>{{ preview.templateContentDigest }}</code></dd>
              <dt>Engagement generation</dt><dd>{{ preview.engagementGeneration }}</dd>
              <dt>Preview digest</dt><dd><code>{{ preview.previewDigest }}</code></dd>
            </dl>
          </details>
          <ol class="plan-steps">
            @for (step of preview.steps; track step.stepKey) {
              <li>
                <details [open]="stepNeedsAttention(step)">
                <summary>{{ step.sortOrder }}. {{ step.title }}</summary>
                <p>Task group: {{ step.taskGroupName ?? 'Ungrouped' }} · {{ step.requiredTask === null ? 'Requiredness unavailable' : !step.requiredTask ? 'Optional' : step.applicabilityCondition ? 'Conditional' : 'Required' }}</p>
                @if (step.applicabilityCondition) { <p>Applies when: {{ step.applicabilityCondition }}</p> }
                <p>{{ step.instructions }}</p>
                <p [id]="planInputId(step.templateStepId, 'schedule')" tabindex="-1"><audit-status [value]="step.phase" /> · {{ step.dueDate ?? 'No due date' }} · {{ step.plannedEffortMinutes ?? 'No effort estimate' }} min@if (!step.dueDate) { · The preview has no date editor; review the engagement schedule or set a task date later. }</p>
                @if (step.dependsOnStepKeys.length) { <p>Prerequisites: {{ step.dependsOnStepKeys.join(', ') }}</p> }
                @if (step.eligibleOwners.length) {
                  <label>Assigned engagement staff
                    <select [id]="planInputId(step.templateStepId, 'owner')" [value]="step.selectedAssigneeUserId ?? ''" (change)="selectOwner(step.stepKey, $any($event.target).value)"
                      [disabled]="busy() || pending() !== null || corruptPending()">
                      @if (!step.selectedAssigneeUserId) { <option value="" disabled>Choose an assignee (optional)</option> }
                      @for (owner of step.eligibleOwners; track owner.userId) {
                        <option [value]="owner.userId">{{ owner.displayName }} · {{ owner.staffingLevel }}</option>
                      }
                    </select>
                  </label>
                  @if (ownerSelections()[step.stepKey]) {
                    <button type="button" matButton (click)="selectOwner(step.stepKey, '')"
                      [disabled]="busy() || pending() !== null || corruptPending() || priorRecovery()"
                      [attr.aria-label]="'Use server suggestion for ' + step.title + ' on next preview'">Use server suggestion on next preview</button>
                  }
                } @else if (step.missingEligibleAssignee) {
                  <p [id]="planInputId(step.templateStepId, 'owner')" tabindex="-1" role="status">No active engagement assignee matches the suggested staffing level. The task will remain unassigned for authorized routing.</p>
                } @else {
                  <p [id]="planInputId(step.templateStepId, 'owner')" tabindex="-1">No eligible engagement staff is currently assigned; the task will remain unassigned.</p>
                }
                @if (step.evidenceRequirements.length) {
                  <section aria-label="Authoritative evidence prerequisites">
                    <h5>Evidence prerequisites</h5>
                    <p>Plan creation binds sources only. Existing source workflows retain approval and review authority.</p>
                    @for (requirement of step.evidenceRequirements; track requirement.requirementKey) {
                      <p><strong>{{ requirement.requirementKey }}</strong> · {{ requirement.kind }} · {{ requirement.sourceState }}</p>
                      <p [id]="planEvidenceStatusId(step.templateStepId, requirement.requirementKey)" tabindex="-1" [attr.role]="requirement.bindingRequired ? 'status' : null">{{ requirement.message }}</p>
                      @if ((requirement.kind === 'PBC_ACCEPTED_OR_CLOSED' || requirement.kind === 'CURRENT_PROCEDURE_REVIEWED') && !preview.alreadyInstantiated) {
                        <div [id]="planEvidenceTargetId(step.templateStepId, requirement.requirementKey)">
                        <audit-workflow-evidence-source-picker
                          [engagementId]="engagementId()"
                          [kind]="requirement.kind"
                          [stepTitle]="'Step ' + step.sortOrder + ': ' + step.title"
                          [requirementKey]="requirement.requirementKey"
                          [pickerId]="evidencePickerId(engagementId(), step.templateStepId, requirement.requirementKey)"
                          [selectedId]="evidenceValue(step.stepKey, requirement.requirementKey, requirement.sourceId)"
                          [disabled]="busy() || pending() !== null || corruptPending()"
                          (sourceSelected)="selectEvidence(step.stepKey, requirement.requirementKey, $event)" />
                        </div>
                      }
                      @if (requirement.kind !== 'CURRENT_MATERIALITY_APPROVED' && !preview.alreadyInstantiated) {
                        <details><summary>Enter an exact source reference manually</summary>
                        <label>Exact source reference for {{ requirement.requirementKey }}
                          <input [value]="evidenceValue(step.stepKey, requirement.requirementKey, requirement.sourceId)"
                            (change)="selectEvidence(step.stepKey, requirement.requirementKey, $any($event.target).value)"
                            maxlength="36" [attr.aria-describedby]="'plan-evidence-help-' + evidencePickerId(engagementId(), step.templateStepId, requirement.requirementKey)"
                            [disabled]="busy() || pending() !== null || corruptPending()" />
                        </label>
                        <small [id]="'plan-evidence-help-' + evidencePickerId(engagementId(), step.templateStepId, requirement.requirementKey)">Use the exact PBC request or procedure identifier from this engagement’s authorized workspace, then preview again.</small>
                        </details>
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
                        @if (pbcSelectionDiffers(step.stepKey, pbc)) {
                          <p [id]="planInputId(step.templateStepId, 'pbc-mismatch')" tabindex="-1" role="status">The selections shown are not yet reflected in the server preview. Complete all three choices and preview again.
                            The current preview shows client owner {{ pbcPreviewName(pbc.clientOwnerUserId, preview.pbcClientOwners) }},
                            firm owner {{ pbcPreviewName(pbc.firmOwnerUserId, preview.pbcFirmOwners) }},
                            and reviewer {{ pbcPreviewName(pbc.reviewerUserId, preview.pbcReviewers) }}.</p>
                        }
                        <div class="pbc-owners">
                          <label>Client owner<select [id]="planInputId(step.templateStepId, 'pbc-client')" [disabled]="busy() || pending() !== null || corruptPending() || priorRecovery()" [value]="pbcOwnerValue(step.stepKey, 'clientOwnerUserId', pbc.clientOwnerUserId)" (change)="selectPbcOwner(step.stepKey, 'clientOwnerUserId', $any($event.target).value)"><option value="" disabled>Choose client owner</option>@for (owner of preview.pbcClientOwners; track owner.userId) { <option [value]="owner.userId">{{ owner.displayName }}</option> }</select></label>
                          <label>Firm owner<select [id]="planInputId(step.templateStepId, 'pbc-firm')" [disabled]="busy() || pending() !== null || corruptPending() || priorRecovery()" [value]="pbcOwnerValue(step.stepKey, 'firmOwnerUserId', pbc.firmOwnerUserId)" (change)="selectPbcOwner(step.stepKey, 'firmOwnerUserId', $any($event.target).value)"><option value="" disabled>Choose firm owner</option>@for (owner of preview.pbcFirmOwners; track owner.userId) { <option [value]="owner.userId">{{ owner.displayName }}</option> }</select></label>
                          <label>Independent reviewer (preview suggestion until confirmed)<select [id]="planInputId(step.templateStepId, 'pbc-reviewer')" [disabled]="busy() || pending() !== null || corruptPending() || priorRecovery()" [value]="pbcOwnerValue(step.stepKey, 'reviewerUserId', pbc.reviewerUserId)" (change)="selectPbcOwner(step.stepKey, 'reviewerUserId', $any($event.target).value)"><option value="" disabled>Choose reviewer</option>@for (owner of preview.pbcReviewers; track owner.userId) { <option [value]="owner.userId">{{ owner.displayName }}</option> }</select></label>
                        </div>
                        @if (pbc.state === 'READY_DRAFT' && pbc.reviewerUserId) {
                          <label><input [id]="planInputId(step.templateStepId, 'pbc-reviewer-confirm')" type="checkbox"
                            [checked]="reviewerConfirmed(preview, step)" [disabled]="busy() || pending() !== null || corruptPending() || priorRecovery() || pbcSelectionDiffers(step.stepKey, pbc) || !reviewerInChoices(preview, pbc.reviewerUserId)"
                            (change)="confirmReviewer(step.stepKey, $any($event.target).checked)" />
                            Confirm {{ pbcPreviewName(pbc.reviewerUserId, preview.pbcReviewers) }} as reviewer for this preview.</label>
                        }
                      }
                      @if (pbc.pbcRequestId) { <p>Linked request <code>{{ pbc.pbcRequestId }}</code> · {{ pbc.pbcRequestState }}</p> }
                      @if (pbc.state === 'BLOCKED_ACTIVATION') { <p role="status">PBC draft creation is deferred until the engagement is active.</p> }
                    } @else { <p role="status">Legacy reference {{ pbc.reference }} is descriptive only and will not create a request.</p> }
                  </section>
                }
                </details>
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
            <button matButton="filled" (click)="createPbcDrafts()" [disabled]="!canCreatePbcDrafts()">Create unsent PBC drafts</button>
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
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);
  private readonly previewTaskPlan = viewChild<unknown, ElementRef<HTMLButtonElement>>('previewTaskPlan', { read: ElementRef });
  private catalogContext: Pick<PlanContext, 'engagementId' | 'session' | 'invalidation'> | null = null;
  private accessEpoch = 0;
  private observedCatalogError = '';
  readonly catalog = this.api.resource(() => {
    const session = this.session.current();
    this.catalogContext = session?.staff ? { engagementId: this.engagementId(), session, invalidation: this.session.invalidation() } : null;
    return `/api/ui/engagements/${this.engagementId()}/task-plan/templates`;
  }, templateCatalog, catalogUnavailable, catalogUnavailable, () => this.catalogDenied(true));
  readonly publishedTemplates = computed<SelectableTemplate[]>(() => (this.accessBlocked() ? [] : this.catalog.data() ?? [])
    .filter((item) => !item.archived && item.currentPublishedRevision?.serviceRoute === this.serviceRoute())
    .map((item) => ({ templateId: item.templateId, revision: item.currentPublishedRevision! })));
  readonly selectedRevisionId = signal('');
  /** Counts over the previewed steps only; it states gaps and changes nothing in the reviewed plan. */
  planSummary(steps: PlanPreview['steps']) {
    return { steps: steps.length, unassigned: steps.filter(step => step.missingEligibleAssignee).length,
      undated: steps.filter(step => step.dueDate === null).length, withPrerequisites: steps.filter(step => step.dependsOnStepKeys.length > 0).length };
  }
  planInputId(stepId: string, suffix: string): string {
    return `plan-${this.engagementId()}-${stepId}-${suffix}`;
  }
  planEvidenceTargetId(stepId: string, requirementKey: string): string {
    const encoded = Array.from(requirementKey, character => character.codePointAt(0)!.toString(16)).join('-') || 'empty';
    return this.planInputId(stepId, `evidence-${encoded}`);
  }
  planEvidenceStatusId(stepId: string, requirementKey: string): string {
    return `${this.planEvidenceTargetId(stepId, requirementKey)}-status`;
  }
  stepNeedsAttention(step: PlanStep): boolean {
    return this.unresolvedInputs().some(item => item.stepId === step.templateStepId);
  }
  focusPlanInput(id: string): void {
    if (!this.plan() || this.busy() || this.pending() || this.corruptPending() || this.priorRecovery() || this.accessBlocked()) return;
    const target = Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>('[id]')).find(element => element.id === id);
    if (!target) return;
    const details = target.closest('details');
    if (details) details.open = true;
    const focusable = target.matches('button,input,select,textarea,[tabindex]') ? target :
      target.querySelector<HTMLElement>('button,input,select,textarea,[tabindex]');
    focusable?.focus();
  }
  readonly referenceUnavailable = computed(() => !!this.suggestedRevisionId() &&
    !this.publishedTemplates().some(item => item.revision.revisionId === this.suggestedRevisionId()));
  readonly plan = signal<PlanPreview | null>(null);
  readonly receipt = signal<PlanReceipt | null>(null);
  readonly pending = signal<PendingPlan | null>(null);
  readonly corruptPending = signal(false);
  readonly priorRecovery = signal(false);
  readonly accessBlocked = signal(false);
  private savedPlan: SavedPlan | null = null;
  private previewContext: PlanContext | null = null;
  private contextVersion = 0;
  private destroyed = false;
  private cleanup: ReservationCleanup | null = null;
  readonly canCleanReservation = signal(false);
  readonly evidenceSelections = signal<Array<{ stepKey: string; requirementKey: string; sourceId: string }>>([]);
  readonly ownerSelections = signal<Record<string, string>>({});
  readonly pbcOwnerSelections = signal<Record<string, PbcOwners>>({});
  private readonly pbcExplicitSelections = signal<Record<string, Partial<PbcOwners>>>({});
  private readonly confirmedReviewers = signal<Record<string, { reviewerId: string; digest: string }>>({});
  readonly pbcRefusal = signal<PbcRefusal | null>(null);
  readonly currentPbcRefusal = computed(() => {
    const refusal = this.pbcRefusal();
    return refusal && this.isCurrent(refusal.context) && this.selectedRevisionId() === refusal.revisionId && !this.accessBlocked() ? refusal : null;
  });
  readonly unresolvedInputs = computed<UnresolvedInput[]>(() => {
    const preview = this.plan();
    if (!preview) return [];
    const items: UnresolvedInput[] = [];
    for (const step of preview.steps) {
      const prefix = `Step ${step.sortOrder}: ${step.title}`;
      const add = (suffix: string, label: string) => items.push({ stepId: step.templateStepId,
        targetId: this.planInputId(step.templateStepId, suffix), label: `${prefix} — ${label}` });
      if (!preview.alreadyInstantiated && !step.selectedAssigneeUserId)
        add('owner', step.eligibleOwners.length ? 'assignee not selected (optional)' : 'no eligible assignee');
      if (!step.dueDate) add('schedule', 'no due date shown');
      for (const requirement of step.evidenceRequirements) {
        if (!preview.alreadyInstantiated && requirement.bindingRequired && !requirement.sourceId && requirement.kind !== 'CURRENT_MATERIALITY_APPROVED')
          items.push({ stepId: step.templateStepId, targetId: this.planEvidenceTargetId(step.templateStepId, requirement.requirementKey),
            label: `${prefix} — select source for ${requirement.requirementKey}` });
        else if (!requirement.satisfied)
          items.push({ stepId: step.templateStepId, targetId: this.planEvidenceStatusId(step.templateStepId, requirement.requirementKey),
            label: `${prefix} — ${requirement.requirementKey}: ${requirement.sourceState} (evidence not yet satisfied)` });
      }
      const pbc = step.pbc;
      if (pbc?.definition && (pbc.state === 'OWNERS_REQUIRED' || pbc.state === 'READY_DRAFT')) {
        if (this.pbcSelectionDiffers(step.stepKey, pbc)) add('pbc-mismatch', 'PBC choices are not reflected in this preview');
        if (!this.pbcOwnerValue(step.stepKey, 'clientOwnerUserId', pbc.clientOwnerUserId))
          add('pbc-client', preview.pbcClientOwners.length ? 'select PBC client owner' : 'no PBC client owner choices shown');
        if (!this.pbcOwnerValue(step.stepKey, 'firmOwnerUserId', pbc.firmOwnerUserId))
          add('pbc-firm', preview.pbcFirmOwners.length ? 'select PBC firm owner' : 'no PBC firm owner choices shown');
        if (!this.pbcOwnerValue(step.stepKey, 'reviewerUserId', pbc.reviewerUserId))
          add('pbc-reviewer', preview.pbcReviewers.length ? 'select PBC reviewer' : 'no PBC reviewer choices shown');
        else if (pbc.state === 'READY_DRAFT' && pbc.reviewerUserId && !this.reviewerInChoices(preview, pbc.reviewerUserId))
          add('pbc-reviewer', 'previewed reviewer is not in current choices');
        else if (pbc.state === 'READY_DRAFT' && pbc.reviewerUserId && !this.pbcSelectionDiffers(step.stepKey, pbc) && !this.reviewerConfirmed(preview, step))
          add('pbc-reviewer-confirm', 'confirm the previewed PBC reviewer');
      }
    }
    return items;
  });
  readonly reviewed = signal(false);
  readonly busy = signal(false);
  readonly uncertain = signal(false);
  readonly failed = signal(false);
  readonly message = signal('');
  readonly canPreview = computed(() => this.publishedTemplates().some((item) => item.revision.revisionId === this.selectedRevisionId()) && !this.busy() &&
    this.pending() === null && !this.corruptPending() && !this.priorRecovery() && !this.accessBlocked());
  readonly canExecute = computed(() => {
    const preview = this.plan();
    return !!preview?.canExecute && !preview.alreadyInstantiated && this.reviewed() && this.previewReady(preview) &&
      !this.busy() && this.pending() === null && !this.corruptPending() && !this.priorRecovery() && !this.accessBlocked();
  });
  readonly canCreatePbcDrafts = computed(() => {
    const preview = this.plan();
    return !!preview?.canCreatePbcDrafts && preview.alreadyInstantiated && this.reviewed() && this.previewReady(preview) &&
      !this.busy() && this.pending() === null && !this.corruptPending() && !this.priorRecovery() && !this.accessBlocked();
  });

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      this.contextVersion++;
      this.cleanup = null;
    });
    effect(() => {
      const engagementId = this.engagementId();
      this.serviceRoute();
      this.suggestedRevisionId();
      const session = this.session.current();
      this.session.invalidation();
      untracked(() => {
        this.contextVersion++;
        this.accessEpoch++;
        this.accessBlocked.set(false);
        this.observedCatalogError = '';
        this.cleanup = null;
        this.canCleanReservation.set(false);
        this.busy.set(false);
        this.selectedRevisionId.set('');
        this.plan.set(null);
        this.receipt.set(null);
        this.ownerSelections.set({});
        this.evidenceSelections.set([]);
        this.pbcOwnerSelections.set({});
        this.pbcExplicitSelections.set({});
        this.confirmedReviewers.set({});
        this.pbcRefusal.set(null);
        this.previewContext = null;
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
          !this.selectedRevisionId() && !this.pending() && !this.corruptPending() && !this.priorRecovery() && !this.busy() && !this.plan() && !this.receipt())
          this.selectTemplate(suggested);
      });
    });
    effect(() => {
      const error = this.catalog.error();
      this.session.current();
      this.session.invalidation();
      this.engagementId();
      untracked(() => {
        if (!error) { this.observedCatalogError = ''; return; }
        if (this.observedCatalogError === error) return;
        this.observedCatalogError = error;
        this.catalogDenied();
      });
    });
  }

  selectTemplate(revisionId: string): void {
    if (this.busy() || this.pending() || this.corruptPending() || this.priorRecovery()) return;
    if (!this.publishedTemplates().some((item) => item.revision.revisionId === revisionId)) return;
    this.selectedRevisionId.set(revisionId);
    this.plan.set(null);
    this.receipt.set(null);
    this.ownerSelections.set({});
        this.evidenceSelections.set([]);
    this.pbcOwnerSelections.set({});
    this.pbcExplicitSelections.set({});
    this.confirmedReviewers.set({});
    this.pbcRefusal.set(null);
    this.previewContext = null;
    this.reviewed.set(false);
    this.message.set('');
  }

  selectOwner(stepKey: string, userId: string): void {
    if (this.busy() || this.pending() || this.corruptPending() || this.priorRecovery()) return;
    this.previewTaskPlan()?.nativeElement.focus();
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
    if (this.busy() || this.pending() || this.corruptPending() || this.priorRecovery()) return;
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
    if (this.busy() || this.pending() || this.corruptPending() || this.priorRecovery()) return;
    this.previewTaskPlan()?.nativeElement.focus();
    const current = { ...this.pbcOwnerSelections() };
    const suggested = this.plan()?.steps.find(step => step.stepKey === stepKey)?.pbc;
    const explicit = { ...this.pbcExplicitSelections(), [stepKey]: { ...this.pbcExplicitSelections()[stepKey], [role]: userId } };
    this.pbcExplicitSelections.set(explicit);
    const selection = this.pbcSelection(explicit[stepKey], suggested);
    if (!selection.clientOwnerUserId && !selection.firmOwnerUserId && !selection.reviewerUserId) delete current[stepKey];
    else current[stepKey] = selection;
    this.pbcOwnerSelections.set(current);
    this.plan.set(null);
    this.reviewed.set(false);
    this.message.set('PBC owners changed. Preview again to review the updated digest.');
  }

  pbcOwnerValue(stepKey: string, role: 'clientOwnerUserId' | 'firmOwnerUserId' | 'reviewerUserId', fallback: string | null): string {
    return this.pbcOwnerSelections()[stepKey]?.[role] || fallback || '';
  }

  private pbcSelection(explicit: Partial<PbcOwners>, suggested: PlanStep['pbc'] | undefined): PbcOwners {
    return { clientOwnerUserId: explicit.clientOwnerUserId || suggested?.clientOwnerUserId || '',
      firmOwnerUserId: explicit.firmOwnerUserId || suggested?.firmOwnerUserId || '',
      reviewerUserId: explicit.reviewerUserId || suggested?.reviewerUserId || '' };
  }

  pbcSelectionDiffers(stepKey: string, pbc: NonNullable<PlanStep['pbc']>): boolean {
    const selected = this.pbcOwnerSelections()[stepKey];
    return !!selected && (selected.clientOwnerUserId !== (pbc.clientOwnerUserId ?? '') ||
      selected.firmOwnerUserId !== (pbc.firmOwnerUserId ?? '') || selected.reviewerUserId !== (pbc.reviewerUserId ?? ''));
  }

  retainedPbcWithoutControls(preview: PlanPreview): Array<{ stepKey: string; label: string; mismatch: boolean }> {
    return Object.keys(this.pbcOwnerSelections()).flatMap(stepKey => {
      const step = preview.steps.find(item => item.stepKey === stepKey);
      const pbc = step?.pbc;
      if (pbc?.definition && (pbc.state === 'OWNERS_REQUIRED' || pbc.state === 'READY_DRAFT')) return [];
      return [{ stepKey, label: step ? `Step ${step.sortOrder}: ${step.title}` : stepKey,
        mismatch: !pbc || this.pbcSelectionDiffers(stepKey, pbc) }];
    });
  }

  clearUnavailablePbcSelection(stepKey: string): void {
    const preview = this.plan();
    if (!preview || !this.previewContext || !this.isCurrent(this.previewContext) ||
        !this.retainedPbcWithoutControls(preview).some(item => item.stepKey === stepKey)) return;
    this.clearLocalPbcSelection(stepKey);
  }

  clearRefusedPbcSelection(stepKey: string): void {
    const refusal = this.pbcRefusal();
    const submitted = refusal?.submitted.find(item => item.stepKey === stepKey);
    const current = this.pbcOwnerSelections()[stepKey];
    if (!refusal || !submitted || !current || !this.isCurrent(refusal.context) ||
        this.selectedRevisionId() !== refusal.revisionId || current.clientOwnerUserId !== submitted.clientOwnerUserId ||
        current.firmOwnerUserId !== submitted.firmOwnerUserId || current.reviewerUserId !== submitted.reviewerUserId) return;
    if (!this.clearLocalPbcSelection(stepKey)) return;
    const remaining = refusal.submitted.filter(item => item.stepKey !== stepKey);
    this.pbcRefusal.set(remaining.length ? { ...refusal, submitted: remaining } : null);
  }

  private clearLocalPbcSelection(stepKey: string): boolean {
    if (this.busy() || this.pending() || this.corruptPending() || this.priorRecovery() || this.accessBlocked() ||
        !this.pbcOwnerSelections()[stepKey]) return false;
    this.previewTaskPlan()?.nativeElement.focus();
    const owners = { ...this.pbcOwnerSelections() };
    const explicit = { ...this.pbcExplicitSelections() };
    delete owners[stepKey];
    delete explicit[stepKey];
    this.pbcOwnerSelections.set(owners);
    this.pbcExplicitSelections.set(explicit);
    this.plan.set(null);
    this.previewContext = null;
    this.confirmedReviewers.set({});
    this.reviewed.set(false);
    this.failed.set(false);
    this.message.set('Local PBC choices cleared for this step. Preview again to review current suggestions.');
    return true;
  }

  pbcPreviewName(userId: string | null, choices: PlanPreview['pbcClientOwners']): string {
    return userId ? choices.find(choice => choice.userId.toLowerCase() === userId.toLowerCase())?.displayName ?? 'not in current choices' : 'not selected';
  }

  reviewerConfirmed(preview: PlanPreview, step: PlanStep): boolean {
    const confirmation = this.confirmedReviewers()[step.templateStepId];
    return !!step.pbc?.reviewerUserId && this.reviewerInChoices(preview, step.pbc.reviewerUserId) && !!confirmation &&
      confirmation.reviewerId === step.pbc.reviewerUserId && confirmation.digest === preview.previewDigest;
  }

  reviewerInChoices(preview: PlanPreview, reviewerId: string): boolean {
    return preview.pbcReviewers.some(candidate => candidate.userId.toLowerCase() === reviewerId.toLowerCase());
  }

  confirmReviewer(stepKey: string, checked: boolean): void {
    const preview = this.plan();
    const step = preview?.steps.find(item => item.stepKey === stepKey);
    const pbc = step?.pbc;
    if (!preview || !step || !pbc || pbc.state !== 'READY_DRAFT' || !pbc.reviewerUserId ||
        !this.reviewerInChoices(preview, pbc.reviewerUserId) || !this.previewContext || !this.isCurrent(this.previewContext) || this.accessBlocked() || this.busy() ||
        this.pending() || this.corruptPending() || this.priorRecovery() || this.pbcSelectionDiffers(stepKey, pbc)) return;
    const confirmations = { ...this.confirmedReviewers() };
    if (checked) confirmations[step.templateStepId] = { reviewerId: pbc.reviewerUserId, digest: preview.previewDigest };
    else delete confirmations[step.templateStepId];
    this.confirmedReviewers.set(confirmations);
    this.reviewed.set(false);
  }

  private previewReady(preview: PlanPreview): boolean {
    if (!this.previewContext || !this.isCurrent(this.previewContext)) return false;
    for (const [stepKey] of Object.entries(this.pbcOwnerSelections())) {
      const pbc = preview.steps.find(step => step.stepKey === stepKey)?.pbc;
      if (!pbc || this.pbcSelectionDiffers(stepKey, pbc)) return false;
    }
    return preview.steps.every(step => step.pbc?.state !== 'READY_DRAFT' || this.reviewerConfirmed(preview, step));
  }

  async previewPlan(): Promise<void> {
    if (!this.canPreview()) return;
    const context = this.captureContext();
    if (!context) return;
    this.previewContext = null;
    this.confirmedReviewers.set({});
    this.plan.set(null);
    this.reviewed.set(false);
    this.busy.set(true);
    this.failed.set(false);
    this.message.set('');
    const revisionId = this.selectedRevisionId();
    const submittedPbcOwners = this.pbcOwnerPayload();
    try {
      const result = await this.api.command<unknown>(this.planUrl('preview'), {
        templateRevisionId: revisionId, owners: this.ownerPayload(), pbcOwners: submittedPbcOwners, evidenceSelections: this.evidenceSelections(),
      });
      if (!this.isCurrent(context)) return;
      if (!result.ok) {
        if (result.status === 401 || result.status === 403) { this.denyAccess(); return; }
        if (!result.unknown && result.code === 'workflow.plan-pbc-owner-ineligible' && submittedPbcOwners.length)
          this.pbcRefusal.set({ context, revisionId, submitted: submittedPbcOwners });
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
      const owners = { ...this.pbcOwnerSelections() };
      for (const [stepKey, explicit] of Object.entries(this.pbcExplicitSelections())) {
        const suggested = preview.steps.find(step => step.stepKey === stepKey)?.pbc;
        if (suggested) owners[stepKey] = this.pbcSelection(explicit, suggested);
      }
      this.pbcRefusal.set(null);
      this.pbcOwnerSelections.set(owners);
      this.previewContext = context;
      this.plan.set(preview);
      this.reviewed.set(false);
      this.message.set('Current plan preview loaded. Review the exact dates, staffing, and digest before creating tasks.');
    } catch {
      if (!this.isCurrent(context)) return;
      this.failed.set(true);
      this.message.set('The preview response could not be verified. Refresh the engagement and preview again.');
    } finally {
      if (this.isCurrent(context)) this.busy.set(false);
    }
  }

  async executePlan(): Promise<void> {
    const preview = this.plan();
    if (!preview || !this.canExecute()) return;
    const context = this.captureContext();
    if (!context) return;
    const pendingPlan: PendingPlan = {
      templateRevisionId: preview.templateRevisionId,
      previewDigest: preview.previewDigest,
      idempotencyKey: crypto.randomUUID(),
    };
    const saved = this.persistPending(context, pendingPlan);
    if (!saved) {
      if (!this.isCurrent(context)) return;
      this.failed.set(true);
      this.message.set('A recovery key could not be saved in this browser, so no task plan was submitted in this attempt.');
      return;
    }
    this.savedPlan = saved;
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
      if (!this.isCurrent(context)) return;
      if (!result.ok && (result.status === 401 || result.status === 403)) { this.denyAccess(); return; }
      if (!this.savedIsCurrent(context, saved)) return;
      if (!result.ok) {
        this.failed.set(true);
        this.uncertain.set(true);
        this.message.set(result.unknown
          ? 'The plan outcome remains unconfirmed. Recover the saved receipt before any new attempt.'
          : 'The response does not establish the outcome of every request using this key. Keep it and recover the receipt before any new attempt.');
        return;
      }
      const confirmed = this.verifyReceipt(result.value, pendingPlan, context.engagementId);
      if (!confirmed) {
        this.uncertain.set(true);
        this.failed.set(true);
        this.message.set('The execute response could not be matched to the saved request. Recover its receipt; do not submit another plan.');
        return;
      }
      this.acceptReceipt(confirmed, context, saved);
    } catch {
      if (!this.isCurrent(context)) return;
      this.uncertain.set(true);
      this.failed.set(true);
      this.message.set('The execute outcome could not be verified. Recover the saved receipt before any new attempt.');
    } finally {
      if (this.isCurrent(context)) this.busy.set(false);
    }
  }

  async recoverReceipt(): Promise<void> {
    const pending = this.pending();
    const saved = this.savedPlan;
    const context = this.captureContext();
    if (!pending || !saved || !context || this.busy() || this.corruptPending()) return;
    if (!this.savedIsCurrent(context, saved)) return;
    this.busy.set(true);
    this.failed.set(false);
    this.message.set('');
    try {
      const raw = await this.api.get(`/api/ui/engagements/${this.engagementId()}/task-plan/receipts/${pending.idempotencyKey}`, planReceipt);
      if (!this.isCurrent(context) || !this.savedIsCurrent(context, saved)) return;
      const confirmed = this.verifyReceipt(raw, pending, context.engagementId);
      if (!confirmed) {
        this.failed.set(true);
        this.uncertain.set(true);
        this.message.set('The receipt did not match the saved request. Keep the recovery key and contact a firm administrator.');
        return;
      }
      this.acceptReceipt(confirmed, context, saved);
    } catch {
      if (!this.isCurrent(context) || this.savedPlan !== saved) return;
      this.denyAccess();
      this.failed.set(true);
      this.uncertain.set(true);
      this.message.set('No receipt was confirmed. Keep this recovery key and retry receipt recovery; do not create another plan.');
    } finally {
      if (this.isCurrent(context)) this.busy.set(false);
    }
  }

  private verifyReceipt(raw: unknown, pending: PendingPlan, engagementId: string): PlanReceipt | null {
    try {
      const receipt = decode(planReceipt, raw);
      return receipt.engagementId === engagementId &&
        receipt.templateRevisionId === pending.templateRevisionId &&
        receipt.requestDigest === pending.previewDigest ? receipt : null;
    } catch { return null; }
  }

  private acceptReceipt(receipt: PlanReceipt, context: PlanContext, saved: SavedPlan): void {
    if (!this.isCurrent(context) || this.savedPlan !== saved) return;
    this.receipt.set(receipt);
    if (!this.clearPending(context, saved)) {
      this.failed.set(true);
      this.message.set('The receipt is confirmed, but the saved recovery marker could not be reconciled. Contact a firm administrator.');
      return;
    }
    this.plan.set(null);
    this.uncertain.set(this.pending() !== null || this.priorRecovery());
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
    if (!preview || !this.canCreatePbcDrafts()) return;
    const context = this.captureContext();
    if (!context) return;
    this.busy.set(true); this.failed.set(false); this.message.set('');
    try {
      const result = await this.api.command<unknown>(this.planUrl('pbc-drafts'), {
        templateRevisionId: preview.templateRevisionId, expectedPreviewDigest: preview.previewDigest,
        pbcOwners: this.pbcOwnerPayload(),
      });
      if (!this.isCurrent(context)) return;
      if (!result.ok) {
        if (result.status === 401 || result.status === 403) { this.denyAccess(); return; }
        this.failed.set(true); this.message.set(result.unknown ? 'The draft outcome was not confirmed. Refresh and recover the retained plan receipt before retrying.' : result.message); return;
      }
      const receipt = decode(planReceipt, result.value);
      if (receipt.engagementId !== this.engagementId() || receipt.templateRevisionId !== preview.templateRevisionId ||
          receipt.pbcDrafts.length === 0 || receipt.pbcDrafts.some(link => !preview.steps.some(step =>
            step.templateStepId === link.templateStepId && step.stepKey === link.stepKey))) {
        this.failed.set(true); this.message.set('The PBC draft response could not be matched to the reviewed plan. Refresh and recover the retained receipt.'); return;
      }
      this.receipt.set(receipt); this.plan.set(null);
      this.message.set(`${receipt.pbcDrafts.length} unsent PBC draft(s) are linked to the retained task plan.`);
    } catch { if (this.isCurrent(context)) { this.failed.set(true); this.message.set('The PBC draft outcome could not be verified. Refresh the engagement and recover its retained plan receipt.'); } }
    finally { if (this.isCurrent(context)) this.busy.set(false); }
  }

  private planUrl(action: string): string {
    return `/api/ui/engagements/${this.engagementId()}/task-plan/${action}`;
  }

  private storageKey(engagementId: string, session: ReturnType<SessionService['current']>): string | null {
    if (!session?.staff || !engagementId) return null;
    return `auditsphere:workflow-plan:${session.firmId}:${session.userId}:${session.generation}:${engagementId}`;
  }

  private captureContext(): PlanContext | null {
    const session = this.session.current();
    return !this.destroyed && session?.staff && this.engagementId()
      ? { engagementId: this.engagementId(), session, invalidation: this.session.invalidation(), version: this.contextVersion,
        accessEpoch: this.accessEpoch, catalogError: this.catalog.error() }
      : null;
  }

  private isIssuingContext(context: PlanContext): boolean {
    return !this.destroyed && this.contextVersion === context.version && this.engagementId() === context.engagementId &&
      this.session.current() === context.session && this.session.invalidation() === context.invalidation;
  }

  private isCurrent(context: PlanContext): boolean {
    return this.isIssuingContext(context) && this.accessEpoch === context.accessEpoch && this.catalog.error() === context.catalogError;
  }

  private catalogDenied(forbidden = false): void {
    const context = this.catalogContext;
    if (this.destroyed || !context || this.session.current() !== context.session || this.engagementId() !== context.engagementId ||
        this.session.invalidation() !== context.invalidation) return;
    if (forbidden) this.observedCatalogError = catalogUnavailable;
    this.denyAccess();
  }

  private denyAccess(): void {
    this.accessEpoch++;
    this.accessBlocked.set(true);
    this.cleanup = null;
    this.canCleanReservation.set(false);
    this.busy.set(false);
    this.plan.set(null);
    this.previewContext = null;
    this.confirmedReviewers.set({});
    this.pbcRefusal.set(null);
    this.receipt.set(null);
    this.reviewed.set(false);
    this.message.set('');
    if (this.pending() || this.savedPlan) this.hideRetainedRecovery();
  }

  private hideRetainedRecovery(): void {
    this.pending.set(null);
    this.savedPlan = null;
    this.priorRecovery.set(true);
    this.uncertain.set(true);
  }

  async refreshPlanAccess(): Promise<void> {
    const context = this.captureContext();
    if (!context || !this.accessBlocked() || this.priorRecovery() || this.corruptPending() || this.busy()) return;
    this.busy.set(true);
    this.failed.set(false);
    this.message.set('');
    try {
      await this.api.get(`/api/ui/engagements/${context.engagementId}/task-plan/templates`, templateCatalog);
      if (!this.isCurrent(context)) return;
      this.catalog.reload();
      this.accessBlocked.set(false);
      this.busy.set(false);
      this.message.set('Access checked. Review a fresh task plan preview.');
    } catch {
      if (!this.isCurrent(context)) return;
      this.denyAccess();
      this.failed.set(true);
      this.message.set('Current task plan access could not be verified.');
    } finally { if (this.isCurrent(context)) this.busy.set(false); }
  }

  // Legacy keys remain authoritative; only inspect records for this exact firm, user and engagement.
  private readSaved(context: PlanContext): SavedPlan[] | null {
    if (!this.isCurrent(context)) return null;
    try {
      const saved: SavedPlan[] = [];
      const storage = sessionStorage;
      const count = storage.length;
      for (let index = 0; index < count; index++) {
        const key = storage.key(index);
        if (!this.isCurrent(context) || key === null) return null;
        const parts = key.split(':');
        if (parts[0] !== 'auditsphere' || parts[1] !== 'workflow-plan' ||
            parts[2]?.toLowerCase() !== context.session.firmId.toLowerCase() ||
            parts[3]?.toLowerCase() !== context.session.userId.toLowerCase()) continue;
        const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
        if (parts.length === 6 && uuid.test(parts[5]) && parts[5].toLowerCase() !== context.engagementId.toLowerCase()) continue;
        if (parts.length !== 6 || !/^[0-9]{1,19}$/.test(parts[4]) ||
            BigInt(parts[4]) > 9223372036854775807n || !uuid.test(parts[5])) return null;
        if (BigInt(parts[4]) > BigInt(context.session.generation)) return null;
        const raw = storage.getItem(key);
        if (!this.isCurrent(context) || raw === null || raw.length > 4096) return null;
        const plan: unknown = JSON.parse(raw);
        if (!isPendingPlan(plan)) return null;
        saved.push({ key, raw, plan, priorGeneration: parts[4] !== context.session.generation, storage });
      }
      if (!this.isCurrent(context) || sessionStorage !== storage || storage.length !== count) return null;
      return saved.sort((a, b) => Number(a.priorGeneration) - Number(b.priorGeneration) || a.key.localeCompare(b.key));
    } catch { return null; }
  }

  private restorePending(engagementId: string, session: ReturnType<SessionService['current']>): void {
    this.pending.set(null);
    this.corruptPending.set(false);
    this.priorRecovery.set(false);
    this.savedPlan = null;
    const context = this.captureContext();
    if (!context || context.engagementId !== engagementId || context.session !== session) return;
    const saved = this.readSaved(context);
    if (!this.isCurrent(context)) return;
    if (!saved) { this.corruptPending.set(true); return; }
    this.priorRecovery.set(saved.length > 0);
    if (saved.length > 0) this.uncertain.set(true);
  }

  async authorizePriorRecovery(): Promise<void> {
    const context = this.captureContext();
    if (!context || !this.priorRecovery() || this.pending() || this.corruptPending() || this.busy()) return;
    this.busy.set(true);
    this.failed.set(false);
    this.message.set('');
    const accessEpoch = this.accessEpoch;
    const catalogError = this.catalog.error();
    try {
      await this.api.get(`/api/ui/engagements/${context.engagementId}/task-plan/templates`, templateCatalog);
      if (!this.isCurrent(context)) return;
      if (accessEpoch !== this.accessEpoch || catalogError !== this.catalog.error()) {
        this.failed.set(true);
        this.message.set('Access changed while checking recovery. Check current access again.');
        return;
      }
      const saved = this.readSaved(context);
      if (!this.isCurrent(context)) return;
      if (accessEpoch !== this.accessEpoch || catalogError !== this.catalog.error()) return;
      if (!saved || !saved.length) {
        this.corruptPending.set(true);
        this.failed.set(true);
        this.message.set('Saved plan recovery could not be verified. Contact a firm administrator.');
        return;
      }
      this.savedPlan = saved[0];
      this.pending.set(saved[0].plan);
      this.uncertain.set(true);
      this.message.set('Access checked. Recover the saved receipt before creating another plan.');
    } catch {
      if (!this.isCurrent(context)) return;
      this.denyAccess();
      this.failed.set(true);
      this.message.set('Current access could not be verified. Saved plan recovery remains unavailable.');
    } finally { if (this.isCurrent(context)) this.busy.set(false); }
  }

  private persistPending(context: PlanContext, pending: PendingPlan): SavedPlan | null {
    const key = this.storageKey(context.engagementId, context.session);
    if (!key || !this.isCurrent(context)) return null;
    const records = this.readSaved(context);
    if (!this.isCurrent(context)) return null;
    if (!records || records.length !== 0) { this.corruptPending.set(true); return null; }
    const raw = JSON.stringify(pending);
    let storage: Storage | null = null;
    let wrote = false;
    try {
      storage = sessionStorage;
      storage.setItem(key, raw);
      wrote = true;
      if (!this.isCurrent(context)) return null;
      if (storage.getItem(key) !== raw || !this.isCurrent(context)) {
        if (this.isCurrent(context)) {
          this.cleanup = { context, key, raw, storage, removalStarted: false };
          this.canCleanReservation.set(true);
          this.corruptPending.set(true);
        }
        return null;
      }
      return { key, raw, plan: pending, priorGeneration: false, storage };
    } catch {
      if (this.isCurrent(context)) {
        if (wrote && storage) {
          this.cleanup = { context, key, raw, storage, removalStarted: false };
          this.canCleanReservation.set(true);
        }
        this.corruptPending.set(true);
      }
      return null;
    }
  }

  retryReservationCleanup(): void {
    const cleanup = this.cleanup;
    if (!cleanup || !this.isCurrent(cleanup.context) || this.busy()) return;
    try {
      if (sessionStorage !== cleanup.storage || !this.isCurrent(cleanup.context)) return;
      const raw = cleanup.storage.getItem(cleanup.key);
      if (!this.isCurrent(cleanup.context)) return;
      if (raw === null && !cleanup.removalStarted || raw !== null && raw !== cleanup.raw) {
        this.failed.set(true);
        this.message.set('The request marker could not be matched. Keep the saved marker for reconciliation.');
        return;
      }
      if (raw === cleanup.raw) {
        cleanup.removalStarted = true;
        cleanup.storage.removeItem(cleanup.key);
        if (!this.isCurrent(cleanup.context)) return;
      }
      if (cleanup.storage.getItem(cleanup.key) !== null || !this.isCurrent(cleanup.context)) {
        if (this.isCurrent(cleanup.context)) this.message.set('The request marker cleanup could not be verified.');
        return;
      }
      this.cleanup = null;
      this.canCleanReservation.set(false);
      this.restorePending(cleanup.context.engagementId, cleanup.context.session);
      if (this.isCurrent(cleanup.context) && !this.corruptPending()) {
        this.uncertain.set(this.pending() !== null || this.priorRecovery());
        this.failed.set(false);
        this.message.set('The unsent reservation was removed. No task plan was submitted in this attempt.');
      }
    } catch {
      if (this.isCurrent(cleanup.context)) {
        this.failed.set(true);
        this.message.set('The request marker cleanup could not be verified. Retry when storage is available.');
      }
    }
  }

  private savedIsCurrent(context: PlanContext, saved: SavedPlan): boolean {
    if (!this.isCurrent(context) || this.savedPlan !== saved) return false;
    const records = this.readSaved(context);
    if (!this.isCurrent(context)) return false;
    if (records?.some(record => record.storage === saved.storage && record.key === saved.key && record.raw === saved.raw)) return true;
    this.corruptPending.set(true);
    this.failed.set(true);
    this.message.set('Saved plan recovery could not be verified. Contact a firm administrator.');
    return false;
  }

  private clearPending(context: PlanContext, saved: SavedPlan): boolean {
    if (!this.isCurrent(context) || this.savedPlan !== saved) return false;
    try {
      if (sessionStorage !== saved.storage || saved.storage.getItem(saved.key) !== saved.raw || !this.isCurrent(context)) {
        if (this.isCurrent(context)) this.corruptPending.set(true);
        return false;
      }
      saved.storage.removeItem(saved.key);
      if (!this.isCurrent(context)) return false;
      if (sessionStorage !== saved.storage || saved.storage.getItem(saved.key) !== null || !this.isCurrent(context)) {
        if (this.isCurrent(context)) this.corruptPending.set(true);
        return false;
      }
      this.restorePending(context.engagementId, context.session);
      return !this.corruptPending();
    } catch {
      if (this.isCurrent(context)) this.corruptPending.set(true);
      return false;
    }
  }
}
