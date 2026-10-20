import { afterNextRender, Component, ElementRef, HostListener, effect, inject, Injector, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { Api, CommandState } from '../../core/api';
import { arr, bool, guid, instant, int, nullable, obj, oneOf, str } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';

const templateItem = obj({
  templateId: guid, latestRevisionId: nullable(guid), version: nullable(int), name: nullable(str(160)),
  serviceRoute: nullable(str(50)), industryKey: nullable(str(80)), revisionStatus: nullable(str(32)),
  archived: bool, createdAt: instant,
});
export const decodeTemplateCatalog = arr(templateItem, 100);

const optionalNullable = <T>(decoder: (value: unknown, path: string) => T) =>
  (value: unknown, path: string): T | null => value === undefined ? null : nullable(decoder)(value, path);
const EVIDENCE_KINDS = [
  ['CURRENT_MATERIALITY_APPROVED', 'Current approved materiality'],
  ['PBC_ACCEPTED_OR_CLOSED', 'Accepted or closed PBC request'],
  ['CURRENT_PROCEDURE_REVIEWED', 'Current reviewed procedure'],
] as const;
const evidenceRequirement = obj({ requirementKey: str(80), kind: oneOf(...EVIDENCE_KINDS.map(kind => kind[0])) });
const evidenceRequirements = (value: unknown, path: string) => {
  const requirements = value === undefined || value === null ? [] : arr(evidenceRequirement, 8)(value, path);
  const keys = new Set<string>();
  for (const requirement of requirements) {
    if (!requirement.requirementKey.trim() || keys.has(requirement.requirementKey))
      throw new Error('Invalid template evidence requirement key.');
    keys.add(requirement.requirementKey);
  }
  return requirements;
};
const templateStep = obj({
  id: guid, stableKey: str(80), phase: str(40), title: str(200), instructions: str(4000),
  suggestedStaffingLevel: nullable(str(32)), estimatedEffortMinutes: nullable(int), dueDateOffsetDays: nullable(int),
  pbcRequestReference: nullable(str(120)), requiredTask: bool, sortOrder: int, dependsOnStepKeys: arr(str(80), 100),
  taskGroupName: optionalNullable(str(120)), evidenceRequirements,
  pbcDefinition: nullable(obj({ objective: str(2000), entityScope: str(500), area: str(200), requestedFormat: str(200),
    controlTotals: nullable(str(2000)), confidentiality: str(100), acceptanceCriteria: str(4000) })),
});
export const decodeTemplateRevision = obj({
  templateId: guid, revisionId: guid, version: int, name: str(160), serviceRoute: str(50),
  industryKey: nullable(str(80)), status: str(32), contentDigest: str(64), revision: int,
  createdByUserId: guid, lastEditedByUserId: guid, reviewedByUserId: nullable(guid),
  createdAt: instant, updatedAt: instant, publishedAt: nullable(instant), archivedAt: nullable(instant),
  steps: arr(templateStep, 500),
});

type TemplateStepDraft = {
  stableKey: string; phase: string; title: string; instructions: string; taskGroupName: string; suggestedStaffingLevel: string;
  estimatedEffortMinutes: number | null; dueDateOffsetDays: number | null; pbcRequestReference: string;
  requiredTask: boolean; sortOrder: number; dependsOnText: string;
  evidenceRequirements: { requirementKey: string; kind: string }[];
  pbcDefinition: { objective: string; entityScope: string; area: string; requestedFormat: string;
    controlTotals: string; confidentiality: string; acceptanceCriteria: string } | null;
};
type TemplateDraft = {
  templateId: string | null; revisionId: string | null; expectedRevision: number | null;
  name: string; serviceRoute: string; industryKey: string; steps: TemplateStepDraft[];
};
type DraftMode = 'new-template' | 'new-revision' | 'edit' | null;

const PHASES = [
  ['CLIENT_ONBOARDING', 'Client onboarding'], ['AUDIT_PLANNING', 'Audit planning'],
  ['AUDIT_FIELDWORK', 'Audit fieldwork'], ['MANAGER_REVIEW', 'Manager review'],
  ['PARTNER_SIGNOFF', 'Partner sign-off'], ['REPORT_DELIVERY', 'Report delivery'], ['ARCHIVE_CLOSE', 'Archive and close'],
] as const;
const STAFFING_LEVELS = [
  ['ENGAGEMENT_PARTNER', 'Engagement Partner'], ['AUDIT_MANAGER', 'Audit Manager'],
  ['SENIOR_AUDITOR', 'Senior Auditor'], ['STAFF_ASSOCIATE', 'Staff Associate'],
] as const;

function emptyStep(sortOrder = 1): TemplateStepDraft {
  return { stableKey: `step-${sortOrder}`, phase: 'AUDIT_FIELDWORK', title: '', instructions: '',
    taskGroupName: '', suggestedStaffingLevel: '', estimatedEffortMinutes: null, dueDateOffsetDays: null,
    pbcRequestReference: '', requiredTask: true, sortOrder, dependsOnText: '', evidenceRequirements: [], pbcDefinition: null };
}
function emptyDraft(templateId: string | null = null): TemplateDraft {
  return { templateId, revisionId: null, expectedRevision: null, name: '', serviceRoute: '', industryKey: '', steps: [emptyStep()] };
}

@Component({
  selector: 'audit-workflow-templates',
  imports: [FormsModule, MatButtonModule, RouterLink, ...SHARED],
  template: `
    <audit-page-header title="Workflow templates" eyebrow="Practice management"
      description="Create versioned operational task plans. Tasks organize work only; they do not satisfy audit, approval, reporting, or archive gates." />
    <p class="muted">Partners and Administrators can prepare, review, publish, and archive firm-wide templates. A different person must review the draft author’s exact revision before it can be published.</p>
    <div class="actions"><a matButton routerLink="/app/work">My work</a>
      <a matButton routerLink="/app/practice/reminder-rules">Reminder rules</a>
      <button matButton="filled" id="new-template-trigger" (click)="startNewTemplate($event)" [disabled]="cmd.busy()">New template</button>
      <button matButton (click)="refresh()" [disabled]="cmd.busy()">Refresh</button>
    </div>
    <audit-state [loading]="catalog.loading()" [error]="catalog.error()" label="workflow templates" />
    <div class="template-layout">
      <section class="panel" aria-labelledby="catalog-heading">
        <h2 id="catalog-heading">Firm template catalog</h2>
        <div class="template-list">
          @for (item of catalog.data() ?? []; track item.templateId) {
            <button matButton [attr.aria-current]="selectedTemplate() === item.templateId ? 'true' : null"
              (click)="selectTemplate(item.templateId)">
              <span>{{ item.name ?? 'Unnamed template' }}</span><small>{{ item.serviceRoute ?? 'No route' }} · {{ item.revisionStatus ?? 'No revision' }}@if (item.version !== null) { · v{{ item.version }} }</small>
              @if (item.archived) { <small>Archived</small> }
            </button>
          } @empty {
            @if (catalog.data()) { <p>No workflow templates are recorded for this firm.</p> }
          }
        </div>
      </section>

      <section class="panel" aria-labelledby="revision-heading">
        @if (draftMode()) {
          <h2 id="revision-heading" tabindex="-1">{{ draftMode() === 'edit' ? 'Edit draft revision' : draftMode() === 'new-revision' ? 'Start next revision' : 'Create template draft' }}</h2>
          <p class="muted">Saving creates or revises a draft only. It does not publish tasks or change existing engagements.</p>
          <form class="template-form" (submit)="$event.preventDefault(); saveDraft()">
            <label>Template name <input name="templateName" [(ngModel)]="draft.name" maxlength="160" required /></label>
            <label>Service route <input name="templateRoute" [(ngModel)]="draft.serviceRoute" maxlength="50" required />
              <small>Use the exact service route supported by this template.</small></label>
            <label>Industry key (optional) <input name="templateIndustry" [(ngModel)]="draft.industryKey" maxlength="80" /></label>
            <h3>Task steps</h3>
            @for (step of draft.steps; track $index; let i = $index) {
              <fieldset class="step-form">
                <legend>Step {{ i + 1 }}</legend>
                <div class="step-grid">
                  <label>Stable key <input [name]="'step-key-' + i" [(ngModel)]="step.stableKey" maxlength="80" required /></label>
                  <label>Workflow phase <select [name]="'step-phase-' + i" [(ngModel)]="step.phase" required>
                    @for (phase of phases; track phase[0]) { <option [value]="phase[0]">{{ phase[1] }}</option> }
                  </select></label>
                  <label class="wide">Task title <input [name]="'step-title-' + i" [(ngModel)]="step.title" maxlength="200" required /></label>
                  <label>Task group (optional) <input [name]="'step-group-' + i" [(ngModel)]="step.taskGroupName" maxlength="120" />
                    <small>Organizes operational tasks within this template revision; it does not change the audit phase or approval gates.</small></label>
                  <label class="wide">Instructions <textarea [name]="'step-instructions-' + i" [(ngModel)]="step.instructions" maxlength="4000" rows="3"></textarea></label>
                  <label>Suggested staffing level <select [name]="'step-staffing-' + i" [(ngModel)]="step.suggestedStaffingLevel">
                    <option value="">Unspecified</option>
                    @for (level of staffingLevels; track level[0]) { <option [value]="level[0]">{{ level[1] }}</option> }
                  </select></label>
                  <label>Effort estimate (minutes) <input type="number" min="1" max="1000000" [name]="'step-effort-' + i" [(ngModel)]="step.estimatedEffortMinutes" /></label>
                  <label>Due-date offset (days) <input type="number" min="-3650" max="3650" [name]="'step-offset-' + i" [(ngModel)]="step.dueDateOffsetDays" /></label>
                  <label>Sort order <input type="number" min="1" max="100000" [name]="'step-order-' + i" [(ngModel)]="step.sortOrder" required /></label>
                  <label>Prerequisite step keys (comma separated) <input [name]="'step-dependencies-' + i" [(ngModel)]="step.dependsOnText" maxlength="8100" /></label>
                  <fieldset class="wide"><legend>Authoritative evidence prerequisites</legend>
                    <p>These requirements block operational task completion until the existing evidence workflow is satisfied. They never approve evidence. Exact PBC or procedure sources are selected for each engagement plan.</p>
                    @for (requirement of step.evidenceRequirements; track $index; let j = $index) {
                      <label>Requirement key <input [name]="'evidence-key-' + i + '-' + j"
                        [(ngModel)]="requirement.requirementKey" maxlength="80" required /></label>
                      <label>Evidence kind <select [name]="'evidence-kind-' + i + '-' + j" [(ngModel)]="requirement.kind" required>
                        <option value="">Choose evidence kind</option>
                        @for (kind of evidenceKinds; track kind[0]) { <option [value]="kind[0]">{{ kind[1] }}</option> }
                      </select></label>
                      <button matButton type="button" (click)="step.evidenceRequirements.splice(j, 1)">Remove requirement {{ j + 1 }}</button>
                    }
                    <button matButton type="button" [disabled]="step.evidenceRequirements.length >= 8"
                      (click)="step.evidenceRequirements.push({ requirementKey: '', kind: '' })">Add evidence prerequisite</button>
                  </fieldset>
                  <label>PBC request reference (optional) <input [name]="'step-pbc-' + i" [(ngModel)]="step.pbcRequestReference" maxlength="120" /></label>
                  @if (!step.pbcDefinition) { <button matButton type="button" (click)="step.pbcDefinition = emptyPbcDefinition()">Add typed PBC instruction</button> }
                  @if (step.pbcDefinition; as pbc) {
                    <fieldset class="wide"><legend>Typed PBC instruction (creates no request by itself)</legend>
                      <label>Objective <textarea [name]="'pbc-objective-' + i" [(ngModel)]="pbc.objective" maxlength="2000" required></textarea></label>
                      <label>Entity scope <input [name]="'pbc-entity-' + i" [(ngModel)]="pbc.entityScope" maxlength="500" required /></label>
                      <label>Area <input [name]="'pbc-area-' + i" [(ngModel)]="pbc.area" maxlength="200" required /></label>
                      <label>Requested format <input [name]="'pbc-format-' + i" [(ngModel)]="pbc.requestedFormat" maxlength="200" required /></label>
                      <label>Control totals (optional) <textarea [name]="'pbc-totals-' + i" [(ngModel)]="pbc.controlTotals" maxlength="2000"></textarea></label>
                      <label>Confidentiality <input [name]="'pbc-confidentiality-' + i" [(ngModel)]="pbc.confidentiality" maxlength="100" required /></label>
                      <label>Acceptance criteria <textarea [name]="'pbc-acceptance-' + i" [(ngModel)]="pbc.acceptanceCriteria" maxlength="4000" required></textarea></label>
                      <button matButton type="button" (click)="step.pbcDefinition = null">Remove typed PBC instruction</button>
                    </fieldset>
                  }
                  <label><span>Required task</span><input type="checkbox" [name]="'step-required-' + i" [(ngModel)]="step.requiredTask" /></label>
                </div>
                <button matButton type="button" (click)="removeStep(i)" [disabled]="draft.steps.length <= 1">Remove step</button>
              </fieldset>
            }
            <div class="actions">
              <button matButton type="button" (click)="addStep()" [disabled]="draft.steps.length >= 500">Add step</button>
              <button matButton="filled" type="submit" [disabled]="cmd.busy() || cmd.uncertain()">Save draft</button>
              <button matButton type="button" (click)="cancelEditing()" [disabled]="cmd.busy()">Cancel</button>
            </div>
          </form>
        } @else if (revision.data(); as r) {
          <h2 id="revision-heading">{{ r.name }} · v{{ r.version }}</h2>
          <p><audit-status [value]="r.status" /> · {{ r.serviceRoute }}@if (r.industryKey) { · {{ r.industryKey }} } · revision {{ r.revision }}</p>
          <p class="muted">Content digest {{ r.contentDigest }}. Published revisions are immutable; existing engagements retain their pinned plan.</p>
          <div class="actions">
            @if (canEditDraft(r)) { <button matButton [id]="'edit-template-' + r.revisionId" (click)="editDraft(r, $event)" [disabled]="cmd.busy()">Edit draft</button> }
            @if (canStartRevision(r)) { <button matButton [id]="'start-template-revision-' + r.revisionId" (click)="startNextRevision(r, $event)" [disabled]="cmd.busy()">Start next revision</button> }
            @if (canCurrentUserReview(r)) { <button matButton (click)="review(r)" [disabled]="cmd.busy() || cmd.uncertain()">Review draft</button> }
            @if (r.status === 'REVIEWED') { <button matButton="filled" (click)="publish(r)" [disabled]="cmd.busy() || cmd.uncertain()">Publish reviewed revision</button> }
            @if (!r.archivedAt && r.status !== 'DRAFT' && r.status !== 'REVIEWED') { <button matButton (click)="archive(r)" [disabled]="cmd.busy() || cmd.uncertain()">Archive template</button> }
          </div>
          @if (r.status === 'DRAFT' || r.status === 'REVIEWED' || r.status === 'PUBLISHED' || r.status === 'SUPERSEDED') {
            <label>Decision rationale <textarea [(ngModel)]="rationale" name="templateRationale" maxlength="1000" rows="2"></textarea></label>
            <p class="muted">Review, publication, and archival require a rationale of 5 to 1,000 characters.</p>
          }
          <h3>Revision steps</h3>
          <ol>@for (step of r.steps; track step.id) {
            <li><strong>{{ step.sortOrder }}. {{ step.title }}</strong> · {{ step.phase }} <audit-status [value]="step.requiredTask ? 'REQUIRED' : 'OPTIONAL'" />
              @if (step.taskGroupName) { <small>Task group: {{ step.taskGroupName }}</small> }
              @if (step.instructions) { <p>{{ step.instructions }}</p> }
              <small>Key {{ step.stableKey }} · {{ step.suggestedStaffingLevel ?? 'No suggested owner' }} · {{ step.estimatedEffortMinutes ?? 'No effort estimate' }} min · {{ step.dueDateOffsetDays ?? 'No due-date offset' }} days</small>
              @for (requirement of step.evidenceRequirements; track requirement.requirementKey) {
                <small>Evidence prerequisite {{ requirement.requirementKey }}: {{ evidenceLabel(requirement.kind) }}</small>
              }
              @if (step.dependsOnStepKeys.length) { <small>Prerequisites: {{ step.dependsOnStepKeys.join(', ') }}</small> }
              @if (step.pbcRequestReference) { <small>PBC reference: {{ step.pbcRequestReference }}</small> }
              @if (step.pbcDefinition; as pbc) { <div class="muted"><strong>Typed PBC instruction</strong><p>{{ pbc.objective }}</p>
                <small>{{ pbc.area }} · {{ pbc.entityScope }} · {{ pbc.requestedFormat }} · {{ pbc.confidentiality }}</small>
                <p>{{ pbc.acceptanceCriteria }}</p></div> }
            </li>
          }</ol>
        } @else if (revision.loading()) {
          <p>Loading template revision…</p>
        } @else if (selectedTemplate()) {
          <audit-state [error]="revision.error()" label="template revision" />
        } @else {
          <h2 id="revision-heading">Select a template</h2><p>Choose a revision from the firm catalog or create a new template draft.</p>
        }
      </section>
    </div>
    <audit-command-message [message]="cmd.message()" [failed]="cmd.failed()" />
  `,
  styleUrl: './workflow-templates.scss',
})
export class WorkflowTemplates {
  readonly evidenceKinds = EVIDENCE_KINDS;
  evidenceLabel(kind: string): string { return EVIDENCE_KINDS.find(item => item[0] === kind)?.[1] ?? kind; }

  private readonly api = inject(Api);
  private readonly session = inject(SessionService);
  private readonly hostElement = inject(ElementRef<HTMLElement>);
  private readonly renderInjector = inject(Injector);
  readonly phases = PHASES;
  readonly staffingLevels = STAFFING_LEVELS;
  readonly catalog = this.api.resource(() => '/api/ui/practice/workflow-templates', decodeTemplateCatalog,
    'Workflow templates are unavailable in this firm scope or the workflow experience is not enabled.');
  readonly selectedTemplate = signal<string | null>(null);
  readonly revision = this.api.resource(() => {
    const templateId = this.selectedTemplate();
    const item = this.catalog.data()?.find(x => x.templateId === templateId);
    return templateId && item?.latestRevisionId
      ? `/api/ui/practice/workflow-templates/${templateId}/revisions/${item.latestRevisionId}` : null;
  }, decodeTemplateRevision, 'The selected workflow revision is unavailable in your current firm scope.');
  readonly cmd = new CommandState(this.api);
  readonly draftMode = signal<DraftMode>(null);
  readonly pendingRevisionId = signal<string | null>(null);
  draft: TemplateDraft = emptyDraft();
  rationale = '';
  private baseline = '';
  private focusReturnId: string | null = null;

  constructor() {
    effect(() => {
      const pending = this.pendingRevisionId();
      const item = this.catalog.data()?.find(x => x.latestRevisionId === pending);
      if (!pending || !item) return;
      this.pendingRevisionId.set(null);
      this.selectedTemplate.set(item.templateId);
      this.revision.reload();
    });
  }

  selectTemplate(templateId: string): void {
    if (!this.confirmNavigation()) return;
    this.selectedTemplate.set(templateId);
    this.revision.reload();
    this.rationale = '';
  }

  startNewTemplate(event?: Event): void {
    if (!this.confirmNavigation()) return;
    this.captureFocusReturnTarget(event);
    this.selectedTemplate.set(null);
    this.draft = emptyDraft();
    this.baseline = JSON.stringify(this.draft);
    this.draftMode.set('new-template');
    this.focusAfterRender('revision-heading');
  }

  editDraft(revision: ReturnType<typeof decodeTemplateRevision> & object, event?: Event): void {
    if (!this.confirmNavigation()) return;
    this.captureFocusReturnTarget(event);
    this.draft = this.fromRevision(revision, revision.revisionId, revision.revision);
    this.baseline = JSON.stringify(this.draft);
    this.draftMode.set('edit');
    this.focusAfterRender('revision-heading');
  }

  startNextRevision(revision: ReturnType<typeof decodeTemplateRevision> & object, event?: Event): void {
    if (!this.confirmNavigation()) return;
    this.captureFocusReturnTarget(event);
    this.draft = this.fromRevision(revision, null, null);
    this.baseline = JSON.stringify(this.draft);
    this.draftMode.set('new-revision');
    this.focusAfterRender('revision-heading');
  }

  addStep(): void {
    if (this.draft.steps.length >= 500) return;
    const order = Math.max(0, ...this.draft.steps.map(x => x.sortOrder)) + 1;
    this.draft.steps = [...this.draft.steps, emptyStep(order)];
  }

  emptyPbcDefinition() {
    return { objective: '', entityScope: '', area: '', requestedFormat: '', controlTotals: '', confidentiality: '', acceptanceCriteria: '' };
  }

  removeStep(index: number): void {
    if (this.draft.steps.length <= 1) return;
    this.draft.steps = this.draft.steps.filter((_, i) => i !== index);
  }

  cancelEditing(): void {
    if (!this.confirmNavigation()) return;
    this.draftMode.set(null);
    this.baseline = '';
    const focusReturnId = this.focusReturnId;
    this.focusReturnId = null;
    if (focusReturnId) this.focusAfterRender(focusReturnId);
  }

  private captureFocusReturnTarget(event?: Event): void {
    const target = event?.currentTarget;
    this.focusReturnId = target instanceof HTMLElement && target.id ? target.id : null;
  }

  private focusAfterRender(id: string): void {
    afterNextRender(() => {
      const host = this.hostElement.nativeElement;
      const target = host.ownerDocument.getElementById(id);
      if (target instanceof HTMLElement && host.contains(target)) target.focus();
    }, { injector: this.renderInjector });
  }

  async saveDraft(): Promise<void> {
    if (!this.draftMode() || this.cmd.busy() || this.cmd.uncertain()) return;
    const mode = this.draftMode();
    const body = {
      templateId: this.draft.templateId,
      name: this.draft.name.trim(), serviceRoute: this.draft.serviceRoute.trim(),
      industryKey: this.draft.industryKey.trim() || null,
      expectedRevision: mode === 'edit' ? this.draft.expectedRevision : null,
      steps: this.draft.steps.map(step => ({
        stableKey: step.stableKey.trim(), phase: step.phase, title: step.title.trim(), instructions: step.instructions.trim(),
        suggestedStaffingLevel: step.suggestedStaffingLevel || null,
        estimatedEffortMinutes: step.estimatedEffortMinutes, dueDateOffsetDays: step.dueDateOffsetDays,
        pbcRequestReference: step.pbcRequestReference.trim() || null, requiredTask: step.requiredTask,
        taskGroupName: step.taskGroupName.trim() || null,
        evidenceRequirements: step.evidenceRequirements.map(requirement => ({ requirementKey: requirement.requirementKey.trim(), kind: requirement.kind })),
        sortOrder: step.sortOrder, dependsOnStepKeys: step.dependsOnText.split(',').map(x => x.trim()).filter(Boolean),
        pbcDefinition: step.pbcDefinition ? { ...step.pbcDefinition,
          controlTotals: step.pbcDefinition.controlTotals.trim() || null } : null,
      })),
    };
    const url = mode === 'edit'
      ? `/api/ui/practice/workflow-templates/${this.draft.templateId}/revisions/${this.draft.revisionId}/edit`
      : '/api/ui/practice/workflow-templates/drafts';
    const created = await this.cmd.run<unknown>(url, body,
      mode === 'edit' ? 'The template draft revision was saved.' : 'The template draft was created.',
      value => {
        if (typeof value === 'string' && /^[0-9a-f-]{36}$/i.test(value)) this.pendingRevisionId.set(value);
        this.draftMode.set(null);
        this.baseline = '';
        this.rationale = '';
        this.catalog.reload();
      });
    if (created && this.selectedTemplate()) this.revision.reload();
  }

  canEditDraft(revision: ReturnType<typeof decodeTemplateRevision>): boolean {
    return revision.status === 'DRAFT' && !revision.archivedAt;
  }

  canStartRevision(revision: ReturnType<typeof decodeTemplateRevision>): boolean {
    const item = this.catalog.data()?.find(x => x.templateId === revision.templateId);
    return !!item && !item.archived && item.latestRevisionId === revision.revisionId &&
      (revision.status === 'PUBLISHED' || revision.status === 'SUPERSEDED');
  }

  canCurrentUserReview(revision: ReturnType<typeof decodeTemplateRevision>): boolean {
    const userId = this.session.current()?.userId;
    return revision.status === 'DRAFT' && !revision.archivedAt && !!userId &&
      userId !== revision.createdByUserId && userId !== revision.lastEditedByUserId;
  }

  review(revision: ReturnType<typeof decodeTemplateRevision>): void {
    this.decide(`/api/ui/practice/workflow-templates/${revision.templateId}/revisions/${revision.revisionId}/review`,
      { expectedRevision: revision.revision, rationale: this.rationale }, 'The template revision was independently reviewed.');
  }

  publish(revision: ReturnType<typeof decodeTemplateRevision>): void {
    this.decide(`/api/ui/practice/workflow-templates/${revision.templateId}/revisions/${revision.revisionId}/publish`,
      { expectedRevision: revision.revision, rationale: this.rationale }, 'The reviewed template revision was published.');
  }

  archive(revision: ReturnType<typeof decodeTemplateRevision>): void {
    this.decide(`/api/ui/practice/workflow-templates/${revision.templateId}/archive`,
      { rationale: this.rationale }, 'The workflow template was archived.');
  }

  refresh(): void {
    if (!this.confirmNavigation()) return;
    this.catalog.reload();
    this.revision.reload();
  }

  confirmNavigation(): boolean {
    if (this.cmd.busy() || this.cmd.uncertain()) {
      this.cmd.message.set('Reconcile the last template command or wait for it to finish before leaving.');
      return false;
    }
    return !this.isDirty() || window.confirm('Discard unsaved workflow template edits?');
  }

  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.cmd.busy() || this.cmd.uncertain() || this.isDirty()) { event.preventDefault(); event.returnValue = ''; }
  }

  private isDirty(): boolean {
    return (!!this.draftMode() && JSON.stringify(this.draft) !== this.baseline) || this.rationale.trim().length > 0;
  }

  private decide(url: string, body: unknown, message: string): void {
    if (this.cmd.busy() || this.cmd.uncertain()) return;
    if (this.rationale.trim().length < 5 || this.rationale.trim().length > 1000) {
      this.cmd.failed.set(true);
      this.cmd.message.set('Enter a decision rationale between 5 and 1,000 characters.');
      return;
    }
    this.cmd.run(url, body, message, () => {
      this.rationale = '';
      this.revision.reload();
      this.catalog.reload();
    });
  }

  private fromRevision(revision: ReturnType<typeof decodeTemplateRevision>, revisionId: string | null,
    expectedRevision: number | null): TemplateDraft {
    return {
      templateId: revision.templateId, revisionId, expectedRevision, name: revision.name,
      serviceRoute: revision.serviceRoute, industryKey: revision.industryKey ?? '',
      steps: revision.steps.map(step => ({
        stableKey: step.stableKey, phase: step.phase, title: step.title, instructions: step.instructions,
        taskGroupName: step.taskGroupName ?? '',
        suggestedStaffingLevel: step.suggestedStaffingLevel ?? '', estimatedEffortMinutes: step.estimatedEffortMinutes,
        dueDateOffsetDays: step.dueDateOffsetDays, pbcRequestReference: step.pbcRequestReference ?? '',
        evidenceRequirements: step.evidenceRequirements.map(requirement => ({ ...requirement })),
        requiredTask: step.requiredTask, sortOrder: step.sortOrder, dependsOnText: step.dependsOnStepKeys.join(', '),
        pbcDefinition: step.pbcDefinition ? { ...step.pbcDefinition, controlTotals: step.pbcDefinition.controlTotals ?? '' } : null,
      })),
    };
  }
}
