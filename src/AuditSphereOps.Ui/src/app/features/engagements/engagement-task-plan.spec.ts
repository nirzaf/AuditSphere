import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/session';
import { EngagementTaskPlan } from './engagement-task-plan';
import { describe, expect, it, vi } from 'vitest';

const firmId = '11111111-1111-4111-8111-111111111111';
const engagementId = '22222222-2222-4222-8222-222222222222';
const templateId = '33333333-3333-4333-8333-333333333333';
const revisionId = '44444444-4444-4444-8444-444444444444';
const alternateEvidenceId = '55555555-5555-4555-8555-555555555555';
const digest = 'a'.repeat(64);
const catalog = [{
  templateId, latestRevisionId: revisionId, version: 1, name: 'Annual audit tasks',
  serviceRoute: 'FinancialStatementAudit', industryKey: null, revisionStatus: 'PUBLISHED',
  archived: false, createdAt: '2026-10-12T09:00:00Z',
  currentPublishedRevision: {
    revisionId, version: 1, name: 'Annual audit tasks', serviceRoute: 'FinancialStatementAudit',
    industryKey: null, contentDigest: digest,
  },
}];
const preview = {
  engagementId, engagementGeneration: 1, templateId, templateRevisionId: revisionId,
  templateVersion: 1, templateName: 'Annual audit tasks', templateContentDigest: digest,
  milestonePlanId: null, milestonePlanRevision: null, scheduleBasisDate: null,
  previewDigest: digest, alreadyInstantiated: false, existingTaskCount: 0,
  templateUpdateNotice: null, engagementStatus: 'Draft', pbcClientOwners: [], pbcFirmOwners: [], pbcReviewers: [],
  canExecute: true, canCreatePbcDrafts: false,
  steps: [{ stepKey: 'planning', templateStepId: revisionId, title: 'Planning', instructions: 'Prepare planning.',
    phase: 'AUDIT_PLANNING', sortOrder: 1, dueDate: null, plannedEffortMinutes: 60,
    suggestedStaffingLevel: null, selectedAssigneeUserId: null, eligibleOwners: [],
    dependsOnStepKeys: [], missingEligibleAssignee: false, existingTaskId: null }],
};

describe('Engagement task plan preview and receipt recovery', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [EngagementTaskPlan],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({ userId: firmId, firmId, generation: '1', staff: true });
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
    sessionStorage.clear();
  });

  function open(suggestedRevisionId: string | null = null) {
    const fixture = TestBed.createComponent(EngagementTaskPlan);
    fixture.componentRef.setInput('engagementId', engagementId);
    fixture.componentRef.setInput('serviceRoute', 'FinancialStatementAudit');
    fixture.componentRef.setInput('suggestedRevisionId', suggestedRevisionId);
    fixture.detectChanges();
    TestBed.tick();
    TestBed.inject(HttpTestingController).expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`).flush(catalog);
    TestBed.tick();
    return fixture;
  }

  it('shows missing inputs before lineage and focuses their existing controls', async () => {
    const fixture = open(), component = fixture.componentInstance;
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    TestBed.inject(HttpTestingController).expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: {
      ...preview, canExecute: false, pbcReviewers: [{ userId: firmId, displayName: 'Reviewer' }], steps: [
        { ...preview.steps[0], eligibleOwners: [{ userId: firmId, displayName: 'Staff member', staffingLevel: 'Staff' }],
          evidenceRequirements: [{ requirementKey: 'request', kind: 'PBC_ACCEPTED_OR_CLOSED', sourceId: null,
            bindingRequired: true, satisfied: false, sourceState: 'SOURCE_REQUIRED', message: 'Select the exact request.',
            sourceRevision: null, inputGeneration: null }],
          pbc: { state: 'OWNERS_REQUIRED', reference: null, definition: { objective: 'Evidence', entityScope: 'Entity', area: 'Planning',
            requestedFormat: 'Document', controlTotals: null, confidentiality: 'Normal', acceptanceCriteria: 'Review' },
            periodStart: '2026-01-01', periodEnd: '2026-12-31', dueDate: null,
            clientOwnerUserId: null, firmOwnerUserId: null, reviewerUserId: null, pbcRequestId: null, pbcRequestState: null } },
        { ...preview.steps[0], stepKey: 'fieldwork', templateStepId: alternateEvidenceId, title: 'Fieldwork', sortOrder: 2,
          dueDate: '2026-12-01', selectedAssigneeUserId: firmId,
          eligibleOwners: [{ userId: firmId, displayName: 'Staff member', staffingLevel: 'Staff' }] },
      ],
    } });
    await previewing;
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const content = host.querySelector<HTMLElement>('.preview')!;
    const inputSection = content.querySelector<HTMLElement>('[aria-labelledby="plan-unresolved-heading"]')!;
    expect(content.querySelector('h3')?.textContent).toContain('Annual audit tasks · v1');
    expect(content.textContent).toContain('Proposed tasks');
    expect(content.textContent).toContain('Schedule basis');
    expect(inputSection.compareDocumentPosition(content.querySelector('details')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    const steps = content.querySelectorAll<HTMLDetailsElement>('.plan-steps > li > details');
    expect(steps[0].open).toBe(true);
    expect(steps[1].open).toBe(false);
    const inputButton = (label: string) => [...inputSection.querySelectorAll('button')].find(button => button.textContent?.includes(label))!;
    steps[0].open = false;
    inputButton('select source for request').click();
    expect(steps[0].open).toBe(true);
    expect(document.activeElement?.getAttribute('aria-label')).toContain('Open evidence source search for request');
    inputButton('assignee not selected').click();
    expect(document.activeElement?.id).toBe(component.planInputId(revisionId, 'owner'));
    inputButton('select PBC reviewer').click();
    expect(document.activeElement?.id).toBe(component.planInputId(revisionId, 'pbc-reviewer'));
    expect(inputSection.textContent).toContain('no PBC client owner choices shown');
    inputButton('no due date shown').click();
    expect(document.activeElement?.id).toBe(component.planInputId(revisionId, 'schedule'));
    expect(document.activeElement?.textContent).toContain('no date editor');
    expect(component.reviewed()).toBe(false);
    expect(component.canExecute()).toBe(false);
    component.reviewed.set(true);
    const owner = host.querySelector<HTMLSelectElement>(`#${component.planInputId(revisionId, 'owner')}`)!;
    owner.value = firmId;
    owner.dispatchEvent(new Event('change'));
    expect(component.plan()).toBeNull();
    expect(component.reviewed()).toBe(false);
    expect(document.activeElement?.textContent?.trim()).toBe('Preview task plan');
  });

  it('keeps a bound but pending source distinct from a missing selection', async () => {
    const fixture = open(), component = fixture.componentInstance;
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    TestBed.inject(HttpTestingController).expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: {
      ...preview, steps: [{ ...preview.steps[0], dueDate: '2026-12-01', selectedAssigneeUserId: firmId,
        eligibleOwners: [{ userId: firmId, displayName: 'Staff member', staffingLevel: 'Staff' }],
        evidenceRequirements: [{ requirementKey: 'request', kind: 'PBC_ACCEPTED_OR_CLOSED', sourceId: templateId,
          bindingRequired: true, satisfied: false, sourceState: 'SENT', message: 'PBC remains pending.', sourceRevision: 1, inputGeneration: null }] }],
    } });
    await previewing;
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const inputs = host.querySelector<HTMLElement>('[aria-labelledby="plan-unresolved-heading"]')!;
    expect(inputs.textContent).not.toContain('select source for request');
    expect(inputs.textContent).toContain('request: SENT (evidence not yet satisfied)');
    const step = host.querySelector<HTMLDetailsElement>('.plan-steps > li > details')!;
    expect(step.open).toBe(true);
    step.open = false;
    [...inputs.querySelectorAll('button')].find(button => button.textContent?.includes('evidence not yet satisfied'))!.click();
    expect(step.open).toBe(true);
    expect(document.activeElement?.id).toBe(component.planEvidenceStatusId(revisionId, 'request'));
    expect(step.textContent).toContain('PBC remains pending.');
    const manual = [...step.querySelectorAll<HTMLDetailsElement>('details')].find(item => item.querySelector('summary')?.textContent?.includes('manually'))!;
    expect(manual.open).toBe(false);
    expect(manual.querySelector('input')?.value).toBe(templateId);
  });

  it('carries server-suggested PBC owners with an explicitly chosen reviewer to the next preview', async () => {
    const fixture = open(), component = fixture.componentInstance, http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const pbc = { state: 'OWNERS_REQUIRED', reference: null, definition: { objective: 'Evidence', entityScope: 'Entity', area: 'Planning',
      requestedFormat: 'Document', controlTotals: null, confidentiality: 'Normal', acceptanceCriteria: 'Review' },
      periodStart: '2026-01-01', periodEnd: '2026-12-31', dueDate: null,
      clientOwnerUserId: templateId, firmOwnerUserId: firmId, reviewerUserId: null, pbcRequestId: null, pbcRequestState: null };
    const first = component.previewPlan();
    const initial = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    expect(initial.request.body.pbcOwners).toEqual([]);
    const response = { ...preview, pbcClientOwners: [{ userId: templateId, displayName: 'Client owner' }],
      pbcFirmOwners: [{ userId: firmId, displayName: 'Firm owner' }],
      pbcReviewers: [{ userId: alternateEvidenceId, displayName: 'Reviewer' }], steps: [{ ...preview.steps[0], pbc }] };
    initial.flush({ value: response });
    await first;
    fixture.detectChanges();
    expect(component.plan()?.steps[0].pbc?.reviewerUserId).toBeNull();
    const reviewer = fixture.nativeElement.querySelector(`#${component.planInputId(revisionId, 'pbc-reviewer')}`) as HTMLSelectElement;
    expect(reviewer.value).toBe('');
    reviewer.value = alternateEvidenceId;
    reviewer.dispatchEvent(new Event('change'));
    expect(component.plan()).toBeNull();
    const next = component.previewPlan();
    const request = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    expect(request.request.body.pbcOwners).toEqual([{ stepKey: 'planning', clientOwnerUserId: templateId,
      firmOwnerUserId: firmId, reviewerUserId: alternateEvidenceId }]);
    request.flush({ value: { ...response, steps: [{ ...preview.steps[0], pbc: { ...pbc, state: 'READY_DRAFT', reviewerUserId: alternateEvidenceId } }] } });
    await next;
    expect(component.plan()?.steps[0].pbc).toMatchObject({ clientOwnerUserId: templateId, firmOwnerUserId: firmId,
      reviewerUserId: alternateEvidenceId, state: 'READY_DRAFT' });
    expect(component.reviewed()).toBe(false);
  });

  it('does not offer unassignment that the preview contract cannot express', async () => {
    const fixture = open(), component = fixture.componentInstance, http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const first = component.previewPlan();
    const eligibleOwners = [{ userId: firmId, displayName: 'Suggested staff', staffingLevel: 'Staff' },
      { userId: alternateEvidenceId, displayName: 'Other staff', staffingLevel: 'Staff' }];
    const response = { ...preview, steps: [{ ...preview.steps[0], selectedAssigneeUserId: firmId, eligibleOwners }] };
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: response });
    await first;
    fixture.detectChanges();
    const select = fixture.nativeElement.querySelector(`#${component.planInputId(revisionId, 'owner')}`) as HTMLSelectElement;
    expect([...select.options].some(option => option.value === '')).toBe(false);
    expect(select.value).toBe(firmId);
    select.value = alternateEvidenceId;
    select.dispatchEvent(new Event('change'));
    const next = component.previewPlan();
    const request = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    expect(request.request.body.owners).toEqual([{ stepKey: 'planning', assigneeUserId: alternateEvidenceId }]);
    request.flush({ value: { ...response, steps: [{ ...response.steps[0], selectedAssigneeUserId: alternateEvidenceId }] } });
    await next;
    expect(component.plan()?.steps[0].selectedAssigneeUserId).toBe(alternateEvidenceId);
    fixture.detectChanges();
    const clear = fixture.nativeElement.querySelector('button[aria-label="Use server suggestion for Planning on next preview"]') as HTMLButtonElement;
    clear.click();
    expect(component.plan()).toBeNull();
    const suggestion = component.previewPlan();
    const defaultRequest = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    expect(defaultRequest.request.body.owners).toEqual([]);
    defaultRequest.flush({ value: response });
    await suggestion;
    expect(component.plan()?.steps[0].selectedAssigneeUserId).toBe(firmId);
    expect(component.reviewed()).toBe(false);
  });

  it('refreshes suggestions while retaining explicit PBC choices when the triple is incomplete', async () => {
    const fixture = open(), component = fixture.componentInstance, http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const pbc = { state: 'OWNERS_REQUIRED', reference: null, definition: { objective: 'Evidence', entityScope: 'Entity', area: 'Planning',
      requestedFormat: 'Document', controlTotals: null, confidentiality: 'Normal', acceptanceCriteria: 'Review' },
      periodStart: '2026-01-01', periodEnd: '2026-12-31', dueDate: null,
      clientOwnerUserId: templateId, firmOwnerUserId: null, reviewerUserId: null, pbcRequestId: null, pbcRequestState: null };
    const response = { ...preview, steps: [{ ...preview.steps[0], pbc }] };
    const first = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: response });
    await first;
    component.selectPbcOwner('planning', 'reviewerUserId', alternateEvidenceId);
    const second = component.previewPlan();
    const incomplete = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    expect(incomplete.request.body.pbcOwners).toEqual([]);
    incomplete.flush({ value: { ...response, steps: [{ ...preview.steps[0], pbc: { ...pbc, clientOwnerUserId: revisionId } }] } });
    await second;
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('selections shown are not yet reflected in the server preview');
    expect(fixture.nativeElement.textContent).toContain('reviewer not selected');
    component.selectPbcOwner('planning', 'firmOwnerUserId', firmId);
    const third = component.previewPlan();
    const request = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    expect(request.request.body.pbcOwners).toEqual([{ stepKey: 'planning', clientOwnerUserId: revisionId,
      firmOwnerUserId: firmId, reviewerUserId: alternateEvidenceId }]);
    request.flush({ value: { ...response, steps: [{ ...preview.steps[0], pbc: { ...pbc, clientOwnerUserId: revisionId,
      firmOwnerUserId: firmId, reviewerUserId: alternateEvidenceId, state: 'READY_DRAFT' } }] } });
    await third;
    expect(component.plan()?.steps[0].pbc?.clientOwnerUserId).toBe(revisionId);
  });

  it.each([false, true])('blocks mismatched PBC choices until a matching preview and reviewer confirmation (existing plan: %s)', async alreadyInstantiated => {
    const fixture = open(), component = fixture.componentInstance, http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const pbc = { state: 'OWNERS_REQUIRED', reference: null, definition: { objective: 'Evidence', entityScope: 'Entity', area: 'Planning',
      requestedFormat: 'Document', controlTotals: null, confidentiality: 'Normal', acceptanceCriteria: 'Review' },
      periodStart: '2026-01-01', periodEnd: '2026-12-31', dueDate: null,
      clientOwnerUserId: templateId, firmOwnerUserId: null, reviewerUserId: null, pbcRequestId: null, pbcRequestState: null };
    const base = { ...preview, alreadyInstantiated, canExecute: !alreadyInstantiated, canCreatePbcDrafts: alreadyInstantiated,
      pbcReviewers: [{ userId: alternateEvidenceId, displayName: 'Chosen reviewer' }, { userId: revisionId, displayName: 'New suggestion' }],
      steps: [{ ...preview.steps[0], pbc }] };
    const first = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: base });
    await first;
    component.selectPbcOwner('planning', 'reviewerUserId', alternateEvidenceId);
    const second = component.previewPlan();
    const unresolved = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    expect(unresolved.request.body.pbcOwners).toEqual([]);
    unresolved.flush({ value: { ...base, steps: [{ ...preview.steps[0], pbc: { ...pbc, state: 'READY_DRAFT',
      firmOwnerUserId: firmId, reviewerUserId: revisionId } }] } });
    await second;
    component.reviewed.set(true);
    await component.executePlan();
    await component.createPbcDrafts();
    expect(component.canExecute()).toBe(false);
    expect(component.canCreatePbcDrafts()).toBe(false);
    expect(sessionStorage.length).toBe(0);
    http.expectNone(request => request.url.endsWith('/execute') || request.url.endsWith('/pbc-drafts'));

    const fresh = component.previewPlan();
    const freshRequest = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    const selected = { stepKey: 'planning', clientOwnerUserId: templateId, firmOwnerUserId: firmId, reviewerUserId: alternateEvidenceId };
    expect(freshRequest.request.body.pbcOwners).toEqual([selected]);
    const freshDigest = 'b'.repeat(64);
    freshRequest.flush({ value: { ...base, previewDigest: freshDigest, steps: [{ ...preview.steps[0], pbc: { ...pbc,
      state: 'READY_DRAFT', firmOwnerUserId: firmId, reviewerUserId: alternateEvidenceId } }] } });
    await fresh;
    component.reviewed.set(true);
    await component.executePlan();
    await component.createPbcDrafts();
    http.expectNone(request => request.url.endsWith('/execute') || request.url.endsWith('/pbc-drafts'));
    component.confirmReviewer('planning', true);
    component.reviewed.set(true);
    expect(alreadyInstantiated ? component.canCreatePbcDrafts() : component.canExecute()).toBe(true);
    const command = alreadyInstantiated ? component.createPbcDrafts() : component.executePlan();
    const sent = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/${alreadyInstantiated ? 'pbc-drafts' : 'execute'}` });
    expect(sent.request.body.expectedPreviewDigest).toBe(freshDigest);
    expect(sent.request.body.pbcOwners).toEqual([selected]);
    sent.flush({ code: 'stale', message: 'stale' }, { status: 409, statusText: 'Conflict' });
    await command;
  });

  it('clears retained choices and permits a fresh inactive plan preview without creating PBC drafts', async () => {
    const fixture = open(), component = fixture.componentInstance, http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const definition = { objective: 'Evidence', entityScope: 'Entity', area: 'Planning', requestedFormat: 'Document',
      controlTotals: null, confidentiality: 'Normal', acceptanceCriteria: 'Review' };
    const pbc = { state: 'OWNERS_REQUIRED', reference: null, definition, periodStart: '2026-01-01', periodEnd: '2026-12-31', dueDate: null,
      clientOwnerUserId: templateId, firmOwnerUserId: null, reviewerUserId: null, pbcRequestId: null, pbcRequestState: null };
    const base = { ...preview, engagementStatus: 'Active', canExecute: false,
      pbcClientOwners: [{ userId: templateId, displayName: 'Client owner' }],
      pbcReviewers: [{ userId: alternateEvidenceId, displayName: 'Reviewer A' }, { userId: revisionId, displayName: 'Reviewer B' }],
      steps: [{ ...preview.steps[0], pbc }] };
    const first = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: base });
    await first;
    component.selectPbcOwner('planning', 'reviewerUserId', alternateEvidenceId);
    const second = component.previewPlan();
    const request = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    expect(request.request.body.pbcOwners).toEqual([]);
    const blocked = { ...pbc, state: 'BLOCKED_ACTIVATION', clientOwnerUserId: revisionId };
    request.flush({ value: { ...base, engagementStatus: 'Draft', canExecute: true, canCreatePbcDrafts: false,
      pbcClientOwners: [{ userId: revisionId, displayName: 'Current client owner' }],
      steps: [{ ...preview.steps[0], pbc: blocked }] } });
    await second;
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const recovery = host.querySelector<HTMLElement>('[aria-label="Retained PBC choices"]')!;
    expect(recovery.textContent).toContain('Choices differ from this preview; commands remain blocked');
    component.reviewed.set(true);
    expect(component.canExecute()).toBe(false);
    await component.executePlan();
    await component.createPbcDrafts();
    http.expectNone(request => request.url.endsWith('/execute') || request.url.endsWith('/pbc-drafts'));
    recovery.querySelector<HTMLButtonElement>('button')!.click();
    expect(component.plan()).toBeNull();
    expect(component.reviewed()).toBe(false);
    http.expectNone(request => request.method === 'POST');
    const fresh = component.previewPlan();
    const freshRequest = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    expect(freshRequest.request.body.pbcOwners).toEqual([]);
    const freshDigest = 'c'.repeat(64);
    freshRequest.flush({ value: { ...base, engagementStatus: 'Draft', canExecute: true, canCreatePbcDrafts: false,
      pbcClientOwners: [{ userId: revisionId, displayName: 'Current client owner' }],
      previewDigest: freshDigest, steps: [{ ...preview.steps[0], pbc: blocked }] } });
    await fresh;
    component.reviewed.set(true);
    expect(component.canCreatePbcDrafts()).toBe(false);
    expect(component.canExecute()).toBe(true);
    const executing = component.executePlan();
    const execute = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/execute` });
    expect(execute.request.body.expectedPreviewDigest).toBe(freshDigest);
    expect(execute.request.body.pbcOwners).toEqual([]);
    http.expectNone(request => request.url.endsWith('/pbc-drafts'));
    execute.flush({ code: 'stale', message: 'stale' }, { status: 409, statusText: 'Conflict' });
    await executing;
  });

  it('clears retained choices for a linked request while preserving confirmation for an actionable draft', async () => {
    const fixture = open(), component = fixture.componentInstance, http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const pbc = { state: 'OWNERS_REQUIRED', reference: null, definition: { objective: 'Evidence', entityScope: 'Entity', area: 'Planning',
      requestedFormat: 'Document', controlTotals: null, confidentiality: 'Normal', acceptanceCriteria: 'Review' },
      periodStart: '2026-01-01', periodEnd: '2026-12-31', dueDate: '2026-11-30',
      clientOwnerUserId: templateId, firmOwnerUserId: firmId, reviewerUserId: null, pbcRequestId: null, pbcRequestState: null };
    const eligibleOwners = [{ userId: firmId, displayName: 'Firm owner', staffingLevel: 'Staff' }];
    const steps = [{ ...preview.steps[0], dueDate: '2026-11-30', existingTaskId: firmId, eligibleOwners, pbc },
      { ...preview.steps[0], stepKey: 'fieldwork', templateStepId: alternateEvidenceId, title: 'Fieldwork',
        sortOrder: 2, dueDate: '2026-11-30', existingTaskId: templateId, eligibleOwners, pbc }];
    const base = { ...preview, engagementStatus: 'Active', alreadyInstantiated: true, canExecute: false, canCreatePbcDrafts: false,
      milestonePlanId: revisionId, milestonePlanRevision: 1, scheduleBasisDate: '2026-11-30',
      existingTaskCount: 2, pbcClientOwners: [{ userId: templateId, displayName: 'Client owner' }],
      pbcFirmOwners: [{ userId: firmId, displayName: 'Firm owner' }],
      pbcReviewers: [{ userId: alternateEvidenceId, displayName: 'Reviewer A' }, { userId: revisionId, displayName: 'Reviewer B' }], steps };
    const first = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: base });
    await first;
    component.selectPbcOwner('fieldwork', 'reviewerUserId', alternateEvidenceId);
    const second = component.previewPlan();
    const selectedDraft = { stepKey: 'fieldwork', clientOwnerUserId: templateId, firmOwnerUserId: firmId, reviewerUserId: alternateEvidenceId };
    const secondRequest = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    expect(secondRequest.request.body.pbcOwners).toEqual([selectedDraft]);
    const ready = { ...pbc, state: 'READY_DRAFT', reviewerUserId: alternateEvidenceId, pbcRequestState: 'DRAFT' };
    secondRequest.flush({ value: { ...base, canCreatePbcDrafts: true, steps: [steps[0], { ...steps[1], pbc: ready }] } });
    await second;
    component.selectPbcOwner('planning', 'reviewerUserId', alternateEvidenceId);
    const third = component.previewPlan();
    const thirdRequest = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    expect(thirdRequest.request.body.pbcOwners).toEqual([selectedDraft,
      { stepKey: 'planning', clientOwnerUserId: templateId, firmOwnerUserId: firmId, reviewerUserId: alternateEvidenceId }]);
    const linked = { ...pbc, state: 'LINKED', reviewerUserId: revisionId, pbcRequestId: revisionId, pbcRequestState: 'SENT' };
    thirdRequest.flush({ value: { ...base, canCreatePbcDrafts: true, steps: [{ ...steps[0], pbc: linked }, { ...steps[1], pbc: ready }] } });
    await third;
    fixture.detectChanges();
    const recovery = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('[aria-label="Retained PBC choices"]')!;
    expect(recovery.textContent).toContain('Choices differ from this preview; commands remain blocked');
    component.confirmReviewer('fieldwork', true);
    component.reviewed.set(true);
    await component.createPbcDrafts();
    http.expectNone(request => request.url.endsWith('/pbc-drafts'));
    recovery.querySelector<HTMLButtonElement>('button')!.click();
    expect(component.reviewed()).toBe(false);
    http.expectNone(request => request.method === 'POST');
    const fresh = component.previewPlan();
    const freshRequest = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    expect(freshRequest.request.body.pbcOwners).toEqual([selectedDraft]);
    const freshDigest = 'c'.repeat(64);
    freshRequest.flush({ value: { ...base, canCreatePbcDrafts: true, previewDigest: freshDigest,
      steps: [{ ...steps[0], pbc: linked }, { ...steps[1], pbc: ready }] } });
    await fresh;
    component.reviewed.set(true);
    expect(component.canCreatePbcDrafts()).toBe(false);
    component.confirmReviewer('fieldwork', true);
    component.reviewed.set(true);
    const creating = component.createPbcDrafts();
    const draft = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/pbc-drafts` });
    expect(draft.request.body.expectedPreviewDigest).toBe(freshDigest);
    expect(draft.request.body.pbcOwners).toEqual([selectedDraft]);
    draft.flush({ code: 'stale', message: 'stale' }, { status: 409, statusText: 'Conflict' });
    await creating;
  });

  it('clears only a chosen submitted PBC step after an ineligible-owner refusal', async () => {
    const fixture = open(), component = fixture.componentInstance, http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const pbc = { state: 'OWNERS_REQUIRED', reference: null, definition: { objective: 'Evidence', entityScope: 'Entity', area: 'Planning',
      requestedFormat: 'Document', controlTotals: null, confidentiality: 'Normal', acceptanceCriteria: 'Review' },
      periodStart: '2026-01-01', periodEnd: '2026-12-31', dueDate: null,
      clientOwnerUserId: templateId, firmOwnerUserId: firmId, reviewerUserId: null, pbcRequestId: null, pbcRequestState: null };
    const steps = [{ ...preview.steps[0], pbc }, { ...preview.steps[0], stepKey: 'fieldwork', templateStepId: alternateEvidenceId,
      title: 'Fieldwork', sortOrder: 2, pbc }];
    const base = { ...preview, pbcReviewers: [{ userId: alternateEvidenceId, displayName: 'Reviewer A' },
      { userId: revisionId, displayName: 'Reviewer B' }], steps };
    const first = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: base });
    await first;
    component.selectPbcOwner('planning', 'reviewerUserId', alternateEvidenceId);
    const second = component.previewPlan();
    const selectedFirst = { stepKey: 'planning', clientOwnerUserId: templateId, firmOwnerUserId: firmId, reviewerUserId: alternateEvidenceId };
    const secondRequest = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    expect(secondRequest.request.body.pbcOwners).toEqual([selectedFirst]);
    secondRequest.flush({ value: { ...base, steps: [{ ...steps[0], pbc: { ...pbc, state: 'READY_DRAFT', reviewerUserId: alternateEvidenceId } }, steps[1]] } });
    await second;
    component.selectPbcOwner('fieldwork', 'reviewerUserId', revisionId);
    const refusing = component.previewPlan();
    const refused = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    const selectedSecond = { stepKey: 'fieldwork', clientOwnerUserId: templateId, firmOwnerUserId: firmId, reviewerUserId: revisionId };
    expect(refused.request.body.pbcOwners).toEqual([selectedFirst, selectedSecond]);
    refused.flush({ code: 'workflow.plan-pbc-owner-ineligible' }, { status: 400, statusText: 'Bad Request' });
    await refusing;
    const retry = component.previewPlan();
    const retryRequest = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    expect(retryRequest.request.body.pbcOwners).toEqual([selectedFirst, selectedSecond]);
    retryRequest.flush({ code: 'unavailable' }, { status: 503, statusText: 'Unavailable' });
    await retry;
    fixture.detectChanges();
    const recovery = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('[aria-label="PBC selection recovery"]')!;
    expect(recovery.textContent).toContain('does not provide a structured step identity');
    [...recovery.querySelectorAll('button')].find(button => button.textContent?.includes('planning'))!.click();
    expect(component.pbcOwnerSelections()['planning']).toBeUndefined();
    expect(component.pbcOwnerSelections()['fieldwork']).toEqual({ clientOwnerUserId: templateId, firmOwnerUserId: firmId, reviewerUserId: revisionId });
    http.expectNone(request => request.method === 'POST');
    const fresh = component.previewPlan();
    const freshRequest = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    expect(freshRequest.request.body.pbcOwners).toEqual([selectedSecond]);
    freshRequest.flush({ value: { ...base, previewDigest: 'd'.repeat(64), steps: [steps[0],
      { ...steps[1], pbc: { ...pbc, state: 'READY_DRAFT', reviewerUserId: revisionId } }] } });
    await fresh;
    expect(component.plan()?.previewDigest).toBe('d'.repeat(64));
    expect(component.reviewed()).toBe(false);
    http.expectNone(request => request.url.endsWith('/execute') || request.url.endsWith('/pbc-drafts'));
  });

  it('cannot use a refusal cleanup after the issuing session changes', async () => {
    const fixture = open(), component = fixture.componentInstance, http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const pbc = { state: 'OWNERS_REQUIRED', reference: null, definition: { objective: 'Evidence', entityScope: 'Entity', area: 'Planning',
      requestedFormat: 'Document', controlTotals: null, confidentiality: 'Normal', acceptanceCriteria: 'Review' },
      periodStart: '2026-01-01', periodEnd: '2026-12-31', dueDate: null,
      clientOwnerUserId: templateId, firmOwnerUserId: firmId, reviewerUserId: null, pbcRequestId: null, pbcRequestState: null };
    const response = { ...preview, steps: [{ ...preview.steps[0], pbc }] };
    const first = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: response });
    await first;
    component.selectPbcOwner('planning', 'reviewerUserId', alternateEvidenceId);
    const rejecting = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` })
      .flush({ code: 'workflow.plan-pbc-owner-ineligible' }, { status: 400, statusText: 'Bad Request' });
    await rejecting;
    expect(component.currentPbcRefusal()?.submitted.length).toBe(1);
    const oldSelection = component.pbcOwnerSelections()['planning'];
    const changedSelection = { ...oldSelection, reviewerUserId: revisionId };
    component.pbcOwnerSelections.set({ planning: changedSelection });
    component.clearRefusedPbcSelection('planning');
    expect(component.pbcOwnerSelections()['planning']).toEqual(changedSelection);
    component.pbcOwnerSelections.set({ planning: oldSelection });
    TestBed.inject(SessionService).current.set({ userId: firmId, firmId, generation: '2', staff: true });
    expect(component.currentPbcRefusal()).toBeNull();
    component.clearRefusedPbcSelection('planning');
    expect(component.pbcOwnerSelections()['planning']).toEqual(oldSelection);
    http.expectNone(request => request.url.endsWith('/execute') || request.url.endsWith('/pbc-drafts'));
    TestBed.tick();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`).flush(catalog);
    TestBed.tick();
    component.selectTemplate(revisionId);
    const fresh = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: response });
    await fresh;
    component.selectPbcOwner('planning', 'reviewerUserId', revisionId);
    const newerSelection = component.pbcOwnerSelections()['planning'];
    component.clearRefusedPbcSelection('planning');
    expect(component.pbcOwnerSelections()['planning']).toEqual(newerSelection);
    expect(component.currentPbcRefusal()).toBeNull();
    http.expectNone(request => request.url.endsWith('/execute') || request.url.endsWith('/pbc-drafts'));
  });

  it('requires explicit confirmation of a suggested reviewer and discards it on a session change', async () => {
    const fixture = open(), component = fixture.componentInstance, http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const pbc = { state: 'READY_DRAFT', reference: null, definition: { objective: 'Evidence', entityScope: 'Entity', area: 'Planning',
      requestedFormat: 'Document', controlTotals: null, confidentiality: 'Normal', acceptanceCriteria: 'Review' },
      periodStart: '2026-01-01', periodEnd: '2026-12-31', dueDate: null,
      clientOwnerUserId: templateId, firmOwnerUserId: firmId, reviewerUserId: alternateEvidenceId, pbcRequestId: null, pbcRequestState: null };
    const previewing = component.previewPlan();
    const request = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    expect(request.request.body.pbcOwners).toEqual([]);
    request.flush({ value: { ...preview, pbcReviewers: [{ userId: alternateEvidenceId, displayName: 'Suggested reviewer' }],
      steps: [{ ...preview.steps[0], pbc }] } });
    await previewing;
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('preview suggestion until confirmed');
    const confirm = host.querySelector<HTMLInputElement>(`#${component.planInputId(revisionId, 'pbc-reviewer-confirm')}`)!;
    expect(confirm.checked).toBe(false);
    component.reviewed.set(true);
    await component.executePlan();
    http.expectNone(request => request.url.endsWith('/execute'));
    confirm.click();
    expect(component.reviewed()).toBe(false);
    component.reviewed.set(true);
    expect(component.canExecute()).toBe(true);
    TestBed.inject(SessionService).current.set({ userId: firmId, firmId, generation: '2', staff: true });
    expect(component.canExecute()).toBe(false);
    await component.executePlan();
    http.expectNone(request => request.url.endsWith('/execute'));
    expect(sessionStorage.length).toBe(0);
    TestBed.tick();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`).flush(catalog);
    TestBed.tick();
    expect(component.plan()).toBeNull();
    expect(component.reviewed()).toBe(false);
    component.selectTemplate(revisionId);
    const nextPreview = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` })
      .flush({ value: { ...preview, pbcReviewers: [{ userId: alternateEvidenceId, displayName: 'Suggested reviewer' }],
        steps: [{ ...preview.steps[0], pbc }] } });
    await nextPreview;
    component.reviewed.set(true);
    expect(component.canExecute()).toBe(false);
    await component.executePlan();
    http.expectNone(request => request.url.endsWith('/execute'));
  });

  it.each(['stepKey', 'templateStepId'] as const)('rejects a preview with duplicate %s across steps', async duplicate => {
    const fixture = open(), component = fixture.componentInstance, http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    const second = { ...preview.steps[0], stepKey: duplicate === 'stepKey' ? 'planning' : 'fieldwork',
      templateStepId: duplicate === 'templateStepId' ? revisionId : alternateEvidenceId, title: 'Fieldwork', sortOrder: 2 };
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` })
      .flush({ value: { ...preview, steps: [preview.steps[0], second] } });
    await previewing;
    fixture.detectChanges();
    expect(component.plan()).toBeNull();
    expect(component.failed()).toBe(true);
    expect(component.canExecute()).toBe(false);
    expect(fixture.nativeElement.querySelector('.preview')).toBeNull();
  });

  it('focuses the exact step when multiple steps have the same evidence requirement key', async () => {
    const fixture = open(), component = fixture.componentInstance, http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const pendingEvidence = { requirementKey: 'request', kind: 'PBC_ACCEPTED_OR_CLOSED', sourceId: templateId,
      bindingRequired: true, satisfied: false, sourceState: 'SENT', message: 'PBC remains pending.', sourceRevision: 1, inputGeneration: null };
    const first = { ...preview.steps[0], evidenceRequirements: [pendingEvidence] };
    const second = { ...preview.steps[0], stepKey: 'fieldwork', templateStepId: alternateEvidenceId, title: 'Fieldwork',
      sortOrder: 2, evidenceRequirements: [pendingEvidence] };
    const previewing = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` })
      .flush({ value: { ...preview, steps: [first, second] } });
    await previewing;
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const inputs = host.querySelector<HTMLElement>('[aria-labelledby="plan-unresolved-heading"]')!;
    [...inputs.querySelectorAll('button')].find(button => button.textContent?.includes('Step 2: Fieldwork — request: SENT'))!.click();
    expect(document.activeElement?.id).toBe(component.planEvidenceStatusId(alternateEvidenceId, 'request'));
    expect(document.activeElement?.id).not.toBe(component.planEvidenceStatusId(revisionId, 'request'));
    expect(component.canExecute()).toBe(false);
  });

  it('binds an exact evidence source to the preview and invalidates prior assent on changes', async () => {
    const fixture = open(), component = fixture.componentInstance;
    component.selectTemplate(revisionId);
    component.selectEvidence('planning', 'pbc-request', templateId);
    const previewing = component.previewPlan();
    const request = TestBed.inject(HttpTestingController).expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    expect(request.request.body.evidenceSelections).toEqual([{ stepKey: 'planning', requirementKey: 'pbc-request', sourceId: templateId }]);
    request.flush({ value: { ...preview, steps: [{ ...preview.steps[0], evidenceRequirements: [
      { requirementKey: 'pbc-request', kind: 'PBC_ACCEPTED_OR_CLOSED', sourceId: templateId, bindingRequired: true,
        satisfied: false, sourceState: 'SENT', message: 'PBC remains pending.', sourceRevision: 1, inputGeneration: null },
      { requirementKey: 'materiality', kind: 'CURRENT_MATERIALITY_APPROVED', sourceId: templateId, bindingRequired: true,
        satisfied: true, sourceState: 'APPROVED', message: 'Materiality was approved.', sourceRevision: 1, inputGeneration: null },
    ] }] } });
    await previewing;
    fixture.detectChanges();
    TestBed.tick();
    const host = fixture.nativeElement as HTMLElement;
    expect([...host.querySelectorAll('label')].some(label => label.textContent?.trim() === 'Exact source reference for pbc-request')).toBe(true);
    expect(host.querySelector('[aria-label^="Evidence source picker for Step 1: Planning — materiality"]')).toBeNull();
    host.querySelector<HTMLButtonElement>('[aria-label="Open evidence source search for request — Step 1: Planning — pbc-request"]')!.click();
    fixture.detectChanges();
    TestBed.tick();
    TestBed.inject(HttpTestingController).expectOne({ method: 'GET', url:
      `/api/ui/engagements/${engagementId}/workflow-evidence-sources?kind=PBC_ACCEPTED_OR_CLOSED&query=&page=0&pageSize=25&selectedId=${templateId}` })
      .flush({ engagementId, kind: 'PBC_ACCEPTED_OR_CLOSED', page: 0, pageSize: 25, hasMore: false,
        items: [{ id: alternateEvidenceId, label: 'Accepted client request', state: 'ACCEPTED', revision: '2' }],
        selected: { id: templateId, label: 'Current request reference', state: 'SENT', revision: '1' } });
    fixture.detectChanges();

    component.reviewed.set(true);
    expect(component.canExecute()).toBe(true);
    host.querySelector<HTMLButtonElement>(`button[aria-label="Select evidence source for request: Accepted client request (${alternateEvidenceId}) — Step 1: Planning — pbc-request"]`)!.click();
    expect(component.evidenceSelections()).toEqual([{ stepKey: 'planning', requirementKey: 'pbc-request', sourceId: alternateEvidenceId }]);
    expect(component.reviewed()).toBe(false); expect(component.plan()).toBeNull(); expect(component.canExecute()).toBe(false);
    expect(document.activeElement?.textContent?.trim()).toBe('Preview task plan');
    component.selectEvidence('planning', 'pbc-request', 'invalid-source');
    expect(component.evidenceSelections()).toEqual([]);
    expect(component.reviewed()).toBe(false); expect(component.plan()).toBeNull(); expect(component.canExecute()).toBe(false);
    component.selectTemplate(revisionId);
    expect(component.evidenceSelections()).toEqual([]);
    const invalidPreview = component.previewPlan();
    TestBed.inject(HttpTestingController).expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` })
      .flush({ value: { ...preview, steps: [{ ...preview.steps[0], evidenceRequirements: [{ requirementKey: 'pbc-request',
        kind: 'PBC_ACCEPTED_OR_CLOSED', sourceId: templateId, bindingRequired: false, satisfied: true,
        sourceState: 'SENT', message: 'Unsupported satisfaction.', sourceRevision: 1, inputGeneration: null }] }] } });
    await invalidPreview;
    expect(component.plan()).toBeNull(); expect(component.failed()).toBe(true); expect(component.canExecute()).toBe(false);
  });

  it('preselects only a current published annual reference without preview or assent', () => {
    const fixture = open(revisionId), component = fixture.componentInstance;
    expect(component.selectedRevisionId()).toBe(revisionId);
    expect(component.plan()).toBeNull(); expect(component.reviewed()).toBe(false);
    expect(component.canExecute()).toBe(false);
    TestBed.inject(HttpTestingController).expectNone(r => r.method === 'POST');
    fixture.componentRef.setInput('suggestedRevisionId', templateId); TestBed.tick();
    expect(component.selectedRevisionId()).toBe(''); expect(component.referenceUnavailable()).toBe(true);
    expect(component.canPreview()).toBe(false);
  });

  it('persists one idempotency key before execute and recovers the matching receipt after an unknown response', async () => {
    const fixture = open();
    const component = fixture.componentInstance;
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    TestBed.inject(HttpTestingController).expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: preview });
    await previewing;
    expect(component.plan()?.canExecute).toBe(true);
    component.reviewed.set(true);

    const executing = component.executePlan();
    const executeRequest = TestBed.inject(HttpTestingController).expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/execute` });
    const idempotencyKey = executeRequest.request.body.idempotencyKey as string;
    expect(idempotencyKey).toMatch(/^[0-9a-f-]{36}$/i);
    expect(sessionStorage.length).toBe(1);
    executeRequest.flush({ code: 'unavailable', message: 'unknown' }, { status: 503, statusText: 'Unavailable' });
    await executing;
    expect(component.uncertain()).toBe(true);
    expect(component.pending()?.idempotencyKey).toBe(idempotencyKey);

    const recovery = component.recoverReceipt();
    TestBed.inject(HttpTestingController).expectOne({
      method: 'GET', url: `/api/ui/engagements/${engagementId}/task-plan/receipts/${idempotencyKey}`,
    }).flush({
      receiptId: templateId, engagementId, templateId, templateRevisionId: revisionId,
      templateVersion: 1, requestDigest: digest, taskCount: 1, replayed: true,
      createdAt: '2026-10-12T09:15:00Z', taskIds: [templateId], pbcDrafts: [],
    });
    await recovery;
    expect(component.receipt()?.taskCount).toBe(1);
    expect(component.pending()).toBeNull();
    expect(component.uncertain()).toBe(false);
    expect(sessionStorage.length).toBe(0);
    component.catalog.reload();
    TestBed.inject(HttpTestingController).expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`)
      .flush({ message: 'unavailable' }, { status: 403, statusText: 'Forbidden' });
    expect(component.receipt()).toBeNull();
    expect(component.accessBlocked()).toBe(true);
  });

  it.each([true, false])('does not let an old execute result change newer recovery (same engagement: %s)', async (sameEngagement) => {
    const fixture = open();
    const component = fixture.componentInstance;
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: preview });
    await previewing;
    component.reviewed.set(true);
    const oldExecute = component.executePlan();
    const oldRequest = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/execute` });
    const oldKey = oldRequest.request.body.idempotencyKey as string;

    const nextEngagement = sameEngagement ? engagementId : '66666666-6666-4666-8666-666666666666';
    const nextUser = alternateEvidenceId;
    const nextPending = { templateRevisionId: revisionId, previewDigest: digest, idempotencyKey: templateId };
    const nextStorageKey = `auditsphere:workflow-plan:${firmId}:${nextUser}:2:${nextEngagement}`;
    const nextRaw = JSON.stringify(nextPending);
    sessionStorage.setItem(nextStorageKey, nextRaw);
    TestBed.inject(SessionService).current.set({ userId: nextUser, firmId, generation: '2', staff: true });
    fixture.componentRef.setInput('engagementId', nextEngagement);
    TestBed.tick();
    http.expectOne(`/api/ui/engagements/${nextEngagement}/task-plan/templates`).flush(catalog);
    TestBed.tick();
    expect(component.pending()).toBeNull();
    const access = component.authorizePriorRecovery();
    http.expectOne(`/api/ui/engagements/${nextEngagement}/task-plan/templates`).flush(catalog);
    await access;
    expect(component.pending()).toEqual(nextPending);
    const nextRecovery = component.recoverReceipt();
    const nextRequest = http.expectOne(`/api/ui/engagements/${nextEngagement}/task-plan/receipts/${templateId}`);
    expect(component.busy()).toBe(true);

    oldRequest.flush({ value: { receiptId: templateId, engagementId, templateId, templateRevisionId: revisionId,
      templateVersion: 1, requestDigest: digest, taskCount: 1, replayed: false,
      createdAt: '2026-10-12T09:15:00Z', taskIds: [templateId], pbcDrafts: [] } });
    await oldExecute;
    expect(component.pending()).toEqual(nextPending);
    expect(component.receipt()).toBeNull();
    expect(component.busy()).toBe(true);
    expect(sessionStorage.getItem(nextStorageKey)).toBe(nextRaw);
    expect(sessionStorage.getItem(`auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`)).toContain(oldKey);

    nextRequest.flush({ receiptId: templateId, engagementId: nextEngagement, templateId, templateRevisionId: revisionId,
      templateVersion: 1, requestDigest: digest, taskCount: 1, replayed: true,
      createdAt: '2026-10-12T09:15:00Z', taskIds: [templateId], pbcDrafts: [] });
    await nextRecovery;
    expect(component.receipt()?.engagementId).toBe(nextEngagement);
    expect(sessionStorage.getItem(nextStorageKey)).toBeNull();
  });

  it('keeps a same-user new-generation request intact when the old execute resolves late', async () => {
    const fixture = open(), component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: preview });
    await previewing;
    component.reviewed.set(true);
    const executing = component.executePlan();
    const old = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/execute` });
    const nextKey = `auditsphere:workflow-plan:${firmId}:${firmId}:2:${engagementId}`;
    const next = { templateRevisionId: revisionId, previewDigest: digest, idempotencyKey: templateId };
    sessionStorage.setItem(nextKey, JSON.stringify(next));
    TestBed.inject(SessionService).current.set({ userId: firmId, firmId, generation: '2', staff: true });
    TestBed.tick();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`).flush(catalog);
    TestBed.tick();
    const authorize = component.authorizePriorRecovery();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`).flush(catalog);
    await authorize;
    expect(component.pending()).toEqual(next);
    const recovering = component.recoverReceipt();
    const read = http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/receipts/${templateId}`);
    old.flush({ value: { receiptId: templateId, engagementId, templateId, templateRevisionId: revisionId,
      templateVersion: 1, requestDigest: digest, taskCount: 1, replayed: false,
      createdAt: '2026-10-12T09:15:00Z', taskIds: [templateId], pbcDrafts: [] } });
    await executing;
    expect(component.pending()).toEqual(next);
    expect(component.busy()).toBe(true);
    expect(sessionStorage.getItem(nextKey)).toBe(JSON.stringify(next));
    read.flush({ code: 'scope.denied', message: 'unavailable' }, { status: 403, statusText: 'Forbidden' });
    await recovering;
    expect(component.pending()).toBeNull();
    expect(sessionStorage.getItem(nextKey)).toBe(JSON.stringify(next));
  });

  it('requires fresh access for a same-generation restored marker and hides it after denied receipt', async () => {
    const key = `auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`;
    const saved = { templateRevisionId: revisionId, previewDigest: digest, idempotencyKey: templateId };
    sessionStorage.setItem(key, JSON.stringify(saved));
    const fixture = open(), component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    expect(component.pending()).toBeNull();
    expect(component.priorRecovery()).toBe(true);
    const denied = component.authorizePriorRecovery();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`)
      .flush({ message: 'unavailable' }, { status: 403, statusText: 'Forbidden' });
    await denied;
    expect(component.pending()).toBeNull();
    expect(sessionStorage.getItem(key)).toBe(JSON.stringify(saved));
    const allowed = component.authorizePriorRecovery();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`).flush(catalog);
    await allowed;
    expect(component.pending()).toEqual(saved);
    component.catalog.reload();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`)
      .flush({ message: 'unavailable' }, { status: 403, statusText: 'Forbidden' });
    expect(component.pending()).toBeNull();
    expect(sessionStorage.getItem(key)).toBe(JSON.stringify(saved));
    const reauthorized = component.authorizePriorRecovery();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`).flush(catalog);
    await reauthorized;
    expect(component.pending()).toEqual(saved);
    const recovery = component.recoverReceipt();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/receipts/${templateId}`)
      .flush({ message: 'unavailable' }, { status: 403, statusText: 'Forbidden' });
    await recovery;
    expect(component.pending()).toBeNull();
    expect(component.priorRecovery()).toBe(true);
    expect(sessionStorage.getItem(key)).toBe(JSON.stringify(saved));
  });

  it('does not reveal recovery when an older successful access check follows a newer catalog denial', async () => {
    const key = `auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`;
    const saved = { templateRevisionId: revisionId, previewDigest: digest, idempotencyKey: templateId };
    sessionStorage.setItem(key, JSON.stringify(saved));
    const fixture = open(), component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    const checking = component.authorizePriorRecovery();
    const olderRead = http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`);
    component.catalog.reload();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`)
      .flush({ message: 'unavailable' }, { status: 403, statusText: 'Forbidden' });
    olderRead.flush(catalog);
    await checking;
    expect(component.pending()).toBeNull();
    expect(component.priorRecovery()).toBe(true);
    expect(sessionStorage.getItem(key)).toBe(JSON.stringify(saved));
    const newer = component.authorizePriorRecovery();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`).flush(catalog);
    await newer;
    expect(component.pending()).toEqual(saved);
  });

  it('discards an older preview success after the current engagement catalog denies access', async () => {
    const fixture = open(), component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    component.ownerSelections.set({ planning: alternateEvidenceId });
    const previewing = component.previewPlan();
    const oldPreview = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` });
    component.catalog.reload();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`)
      .flush({ message: 'unavailable' }, { status: 403, statusText: 'Forbidden' });
    expect(component.accessBlocked()).toBe(true);
    expect(component.publishedTemplates()).toEqual([]);
    oldPreview.flush({ value: preview });
    await previewing;
    fixture.detectChanges();
    expect(component.plan()).toBeNull();
    expect(component.reviewed()).toBe(false);
    expect(component.canExecute()).toBe(false);
    expect(component.ownerSelections()).toEqual({ planning: alternateEvidenceId });
    expect((fixture.nativeElement as HTMLElement).querySelector('.preview')).toBeNull();
    expect(component.busy()).toBe(false);
    const access = component.refreshPlanAccess();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`).flush(catalog);
    await access;
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`).flush(catalog);
    TestBed.tick();
    expect(component.accessBlocked()).toBe(false);
    expect(component.plan()).toBeNull();
    expect(component.canExecute()).toBe(false);
  });

  it('discards an older PBC draft success after a newer catalog denial', async () => {
    const fixture = open(), component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` })
      .flush({ value: { ...preview, alreadyInstantiated: true, canCreatePbcDrafts: true } });
    await previewing;
    component.reviewed.set(true);
    const creating = component.createPbcDrafts();
    const oldResult = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/pbc-drafts` });
    component.catalog.reload();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`)
      .flush({ message: 'unavailable' }, { status: 403, statusText: 'Forbidden' });
    oldResult.flush({ value: { receiptId: templateId, engagementId, templateId, templateRevisionId: revisionId,
      templateVersion: 1, requestDigest: digest, taskCount: 1, replayed: true,
      createdAt: '2026-10-12T09:15:00Z', taskIds: [templateId],
      pbcDrafts: [{ stepKey: 'planning', templateStepId: revisionId, taskId: templateId, pbcRequestId: alternateEvidenceId }] } });
    await creating;
    fixture.detectChanges();
    expect(component.receipt()).toBeNull();
    expect(component.plan()).toBeNull();
    expect(component.reviewed()).toBe(false);
    expect(component.accessBlocked()).toBe(true);
    expect((fixture.nativeElement as HTMLElement).querySelector('.receipt')).toBeNull();
    expect(component.busy()).toBe(false);
  });

  it('retains a submitted marker when an older execute success follows a catalog denial', async () => {
    const fixture = open(), component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: preview });
    await previewing;
    component.reviewed.set(true);
    const executing = component.executePlan();
    const oldResult = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/execute` });
    const storageKey = `auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`;
    const stored = sessionStorage.getItem(storageKey);
    component.catalog.reload();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`)
      .flush({ message: 'unavailable' }, { status: 403, statusText: 'Forbidden' });
    expect(component.pending()).toBeNull();
    expect(component.priorRecovery()).toBe(true);
    oldResult.flush({ value: { receiptId: templateId, engagementId, templateId, templateRevisionId: revisionId,
      templateVersion: 1, requestDigest: digest, taskCount: 1, replayed: false,
      createdAt: '2026-10-12T09:15:00Z', taskIds: [templateId], pbcDrafts: [] } });
    await executing;
    expect(sessionStorage.getItem(storageKey)).toBe(stored);
    expect(component.receipt()).toBeNull();
    expect(component.accessBlocked()).toBe(true);
    expect(component.canExecute()).toBe(false);
  });

  it('retains a submitted marker when the issuing component is destroyed before its late success', async () => {
    const fixture = open(), component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: preview });
    await previewing;
    component.reviewed.set(true);
    const executing = component.executePlan();
    const oldResult = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/execute` });
    const requestKey = oldResult.request.body.idempotencyKey as string;
    const storageKey = `auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`;
    const stored = sessionStorage.getItem(storageKey);
    fixture.destroy();

    const replacement = open(), live = replacement.componentInstance;
    expect(live.pending()).toBeNull();
    const authorize = live.authorizePriorRecovery();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`).flush(catalog);
    await authorize;
    expect(live.pending()?.idempotencyKey).toBe(requestKey);
    oldResult.flush({ value: { receiptId: templateId, engagementId, templateId, templateRevisionId: revisionId,
      templateVersion: 1, requestDigest: digest, taskCount: 1, replayed: false,
      createdAt: '2026-10-12T09:15:00Z', taskIds: [templateId], pbcDrafts: [] } });
    await executing;
    expect(sessionStorage.getItem(storageKey)).toBe(stored);
    expect(live.pending()?.idempotencyKey).toBe(requestKey);
    expect(live.receipt()).toBeNull();
    http.expectNone(request => request.url.endsWith('/execute'));
  });

  it('retains a prior-generation request until a fresh authorized engagement read and exact receipt', async () => {
    const priorKey = `auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`;
    const prior = { templateRevisionId: revisionId, previewDigest: digest, idempotencyKey: templateId };
    sessionStorage.setItem(priorKey, JSON.stringify(prior));
    const otherKey = `auditsphere:workflow-plan:${firmId}:${alternateEvidenceId}:1:${engagementId}`;
    sessionStorage.setItem(otherKey, '{not this user');
    TestBed.inject(SessionService).current.set({ userId: firmId, firmId, generation: '2', staff: true });
    const fixture = open(), component = fixture.componentInstance;
    expect(component.priorRecovery()).toBe(true);
    expect(component.pending()).toBeNull();
    component.selectTemplate(revisionId);
    expect(component.canPreview()).toBe(false);
    const denied = component.authorizePriorRecovery();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`)
      .flush({ message: 'unavailable' }, { status: 403, statusText: 'Forbidden' });
    await denied;
    expect(component.pending()).toBeNull();
    expect(sessionStorage.getItem(priorKey)).toBe(JSON.stringify(prior));
    const allowed = component.authorizePriorRecovery();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`).flush(catalog);
    await allowed;
    expect(component.pending()).toEqual(prior);
    const recovery = component.recoverReceipt();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/receipts/${templateId}`).flush({
      receiptId: templateId, engagementId, templateId, templateRevisionId: revisionId,
      templateVersion: 1, requestDigest: digest, taskCount: 1, replayed: true,
      createdAt: '2026-10-12T09:15:00Z', taskIds: [templateId], pbcDrafts: [],
    });
    await recovery;
    expect(component.receipt()?.taskCount).toBe(1);
    expect(sessionStorage.getItem(priorKey)).toBeNull();
    expect(sessionStorage.getItem(otherKey)).toBe('{not this user');
    http.expectNone(request => request.url.endsWith('/execute'));
  });

  it('preserves each prior-generation marker when one saved receipt is recovered', async () => {
    const firstKey = `auditsphere:workflow-plan:${firmId}:${firmId}:0:${engagementId}`;
    const secondKey = `auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`;
    const first = { templateRevisionId: revisionId, previewDigest: digest, idempotencyKey: templateId };
    const second = { templateRevisionId: revisionId, previewDigest: digest, idempotencyKey: alternateEvidenceId };
    sessionStorage.setItem(firstKey, JSON.stringify(first));
    sessionStorage.setItem(secondKey, JSON.stringify(second));
    TestBed.inject(SessionService).current.set({ userId: firmId, firmId, generation: '2', staff: true });
    const fixture = open(), component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    const authorizeFirst = component.authorizePriorRecovery();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`).flush(catalog);
    await authorizeFirst;
    expect(component.pending()).toEqual(first);
    const recovery = component.recoverReceipt();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/receipts/${templateId}`).flush({
      receiptId: templateId, engagementId, templateId, templateRevisionId: revisionId,
      templateVersion: 1, requestDigest: digest, taskCount: 1, replayed: true,
      createdAt: '2026-10-12T09:15:00Z', taskIds: [templateId], pbcDrafts: [],
    });
    await recovery;
    expect(sessionStorage.getItem(firstKey)).toBeNull();
    expect(sessionStorage.getItem(secondKey)).toBe(JSON.stringify(second));
    expect(component.pending()).toBeNull();
    expect(component.priorRecovery()).toBe(true);
    const authorizeSecond = component.authorizePriorRecovery();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`).flush(catalog);
    await authorizeSecond;
    expect(component.pending()).toEqual(second);
    http.expectNone(request => request.url.endsWith('/execute'));
  });

  it('refuses dispatch when a same-owner marker is malformed or storage is unavailable', async () => {
    const fixture = open(), component = fixture.componentInstance;
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: preview });
    await previewing;
    component.reviewed.set(true);
    const badKey = `auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`;
    sessionStorage.setItem(badKey, '{invalid');
    await component.executePlan();
    expect(component.corruptPending()).toBe(true);
    expect(sessionStorage.getItem(badKey)).toBe('{invalid');
    http.expectNone(request => request.url.endsWith('/execute'));

    sessionStorage.removeItem(badKey);
    const second = open(), secondComponent = second.componentInstance;
    secondComponent.selectTemplate(revisionId);
    const secondPreview = secondComponent.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: preview });
    await secondPreview;
    secondComponent.reviewed.set(true);
    const storageRead = vi.spyOn(Storage.prototype, 'key').mockImplementation(() => { throw new Error('unavailable'); });
    // An unrelated key ensures the scan exercises the storage read.
    sessionStorage.setItem('unrelated', 'value');
    try {
      await secondComponent.executePlan();
      expect(secondComponent.corruptPending()).toBe(true);
      http.expectNone(request => request.url.endsWith('/execute'));
    } finally { storageRead.mockRestore(); }
  });

  it('retains a reservation if its write succeeds but readback fails before dispatch', async () => {
    const fixture = open(), component = fixture.componentInstance;
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: preview });
    await previewing;
    component.reviewed.set(true);
    const read = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('unavailable'); });
    try {
      await component.executePlan();
      expect(component.corruptPending()).toBe(true);
      http.expectNone(request => request.url.endsWith('/execute'));
    } finally { read.mockRestore(); }
    const key = `auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`;
    expect(sessionStorage.getItem(key)).toContain('idempotencyKey');
    expect(component.canCleanReservation()).toBe(true);
    component.retryReservationCleanup();
    expect(sessionStorage.getItem(key)).toBeNull();
    expect(component.corruptPending()).toBe(false);
    http.expectNone(request => request.url.endsWith('/execute'));
  });

  it('cleans only the exact uninvoked reservation and reconciles an uncertain removal', async () => {
    const fixture = open(), component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: preview });
    await previewing;
    component.reviewed.set(true);
    const read = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('unavailable'); });
    try { await component.executePlan(); } finally { read.mockRestore(); }
    const key = `auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`;
    const original = sessionStorage.getItem(key)!;
    const replacement = JSON.stringify({ templateRevisionId: revisionId, previewDigest: digest, idempotencyKey: templateId });
    sessionStorage.setItem(key, replacement);
    component.retryReservationCleanup();
    expect(sessionStorage.getItem(key)).toBe(replacement);
    expect(component.canCleanReservation()).toBe(true);
    sessionStorage.setItem(key, original);
    const removeOriginal = Storage.prototype.removeItem;
    const remove = vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(function(this: Storage, item: string) {
      removeOriginal.call(this, item);
      throw new Error('readback unavailable');
    });
    try { component.retryReservationCleanup(); } finally { remove.mockRestore(); }
    expect(sessionStorage.getItem(key)).toBeNull();
    expect(component.corruptPending()).toBe(true);
    expect(component.canCleanReservation()).toBe(true);
    component.retryReservationCleanup();
    expect(component.corruptPending()).toBe(false);
    expect(component.canCleanReservation()).toBe(false);
    http.expectNone(request => request.url.endsWith('/execute'));
  });

  it('does not retain cleanup capability after the issuing session changes', async () => {
    const fixture = open(), component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: preview });
    await previewing;
    component.reviewed.set(true);
    const read = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('unavailable'); });
    try { await component.executePlan(); } finally { read.mockRestore(); }
    const key = `auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`;
    const raw = sessionStorage.getItem(key);
    expect(component.canCleanReservation()).toBe(true);
    TestBed.inject(SessionService).current.set({ userId: firmId, firmId, generation: '2', staff: true });
    TestBed.tick();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`).flush(catalog);
    TestBed.tick();
    expect(component.canCleanReservation()).toBe(false);
    component.retryReservationCleanup();
    expect(sessionStorage.getItem(key)).toBe(raw);
    expect(component.priorRecovery()).toBe(true);
    http.expectNone(request => request.url.endsWith('/execute'));
  });

  it('ignores a malformed generation for a definitely different engagement but fences an ambiguous key', () => {
    const other = '66666666-6666-4666-8666-666666666666';
    const otherKey = `auditsphere:workflow-plan:${firmId}:${firmId}:bad-generation:${other}`;
    sessionStorage.setItem(otherKey, '{invalid');
    const first = open();
    expect(first.componentInstance.corruptPending()).toBe(false);
    first.componentInstance.selectTemplate(revisionId);
    expect(first.componentInstance.canPreview()).toBe(true);
    const ambiguous = `auditsphere:workflow-plan:${firmId}:${firmId}:bad-generation:not-an-engagement`;
    sessionStorage.setItem(ambiguous, '{invalid');
    const second = open();
    expect(second.componentInstance.corruptPending()).toBe(true);
    expect(sessionStorage.getItem(otherKey)).toBe('{invalid');
    expect(sessionStorage.getItem(ambiguous)).toBe('{invalid');
  });

  it('does not delete or replace a changed recovery marker when a response arrives', async () => {
    const fixture = open(), component = fixture.componentInstance;
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: preview });
    await previewing;
    component.reviewed.set(true);
    const executing = component.executePlan();
    const request = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/execute` });
    const key = `auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`;
    const replacement = JSON.stringify({ templateRevisionId: revisionId, previewDigest: digest, idempotencyKey: templateId });
    sessionStorage.setItem(key, replacement);
    request.flush({ value: { receiptId: templateId, engagementId, templateId, templateRevisionId: revisionId,
      templateVersion: 1, requestDigest: digest, taskCount: 1, replayed: false,
      createdAt: '2026-10-12T09:15:00Z', taskIds: [templateId], pbcDrafts: [] } });
    await executing;
    expect(component.corruptPending()).toBe(true);
    expect(component.receipt()).toBeNull();
    expect(sessionStorage.getItem(key)).toBe(replacement);
  });

  it('keeps the original request when the server reports an idempotency conflict', async () => {
    const fixture = open(), component = fixture.componentInstance;
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: preview });
    await previewing;
    component.reviewed.set(true);
    const executing = component.executePlan();
    const request = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/execute` });
    const key = request.request.body.idempotencyKey as string;
    request.flush({ code: 'idempotency.conflict', message: 'conflict' }, { status: 409, statusText: 'Conflict' });
    await executing;
    expect(component.pending()?.idempotencyKey).toBe(key);
    expect(component.uncertain()).toBe(true);
    expect(component.canExecute()).toBe(false);
    const storageKey = `auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`;
    const original = sessionStorage.getItem(storageKey);
    expect(original).toContain(key);
    const recovery = component.recoverReceipt();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/receipts/${key}`)
      .flush({ code: 'scope.denied', message: 'Access denied.' }, { status: 403, statusText: 'Forbidden' });
    await recovery;
    expect(sessionStorage.getItem(storageKey)).toBe(original);
    expect(component.priorRecovery()).toBe(true);
    http.expectNone(request => request.url.endsWith('/execute'));
  });

  it('retains a stale-refused key and recovers a matching earlier receipt without another execute', async () => {
    const fixture = open(), component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: preview });
    await previewing;
    component.reviewed.set(true);
    const executing = component.executePlan();
    const request = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/execute` });
    const requestKey = request.request.body.idempotencyKey as string;
    const storageKey = `auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`;
    const original = sessionStorage.getItem(storageKey);
    request.flush({ code: 'revision.stale', message: 'Changed.' }, { status: 409, statusText: 'Conflict' });
    await executing;
    expect(component.pending()?.idempotencyKey).toBe(requestKey);
    expect(component.uncertain()).toBe(true);
    expect(component.canExecute()).toBe(false);
    expect(sessionStorage.getItem(storageKey)).toBe(original);
    http.expectNone(request => request.url.endsWith('/execute'));
    const recovery = component.recoverReceipt();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/receipts/${requestKey}`).flush({
      receiptId: templateId, engagementId, templateId, templateRevisionId: revisionId,
      templateVersion: 1, requestDigest: digest, taskCount: 1, replayed: true,
      createdAt: '2026-10-12T09:15:00Z', taskIds: [templateId], pbcDrafts: [],
    });
    await recovery;
    expect(component.receipt()?.replayed).toBe(true);
    expect(component.pending()).toBeNull();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
    http.expectNone(request => request.url.endsWith('/execute'));
  });

  it('retains the submitted key after a validation refusal', async () => {
    const fixture = open(), component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: preview });
    await previewing;
    component.reviewed.set(true);
    const executing = component.executePlan();
    const request = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/execute` });
    const requestKey = request.request.body.idempotencyKey as string;
    const storageKey = `auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`;
    const original = sessionStorage.getItem(storageKey);
    request.flush({ code: 'workflow.plan-invalid', message: 'Invalid.' }, { status: 400, statusText: 'Bad Request' });
    await executing;
    expect(component.pending()?.idempotencyKey).toBe(requestKey);
    expect(component.uncertain()).toBe(true);
    expect(component.canExecute()).toBe(false);
    expect(sessionStorage.getItem(storageKey)).toBe(original);
    http.expectNone(request => request.url.endsWith('/execute'));
  });

  it('retains the request after a scope-denied execute response', async () => {
    const fixture = open(), component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: preview });
    await previewing;
    component.reviewed.set(true);
    const executing = component.executePlan();
    const request = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/execute` });
    const key = `auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`;
    const original = sessionStorage.getItem(key);
    expect(original).toContain(request.request.body.idempotencyKey);
    request.flush({ code: 'scope.denied', message: 'Access denied.' }, { status: 403, statusText: 'Forbidden' });
    await executing;
    expect(sessionStorage.getItem(key)).toBe(original);
    expect(component.pending()).toBeNull();
    expect(component.priorRecovery()).toBe(true);
    expect(component.accessBlocked()).toBe(true);
    expect(component.corruptPending()).toBe(false);
    expect(component.canCleanReservation()).toBe(false);
    http.expectNone(request => request.url.endsWith('/execute'));
  });

  it('never cleans a changed marker or one from a later context after a scope refusal', async () => {
    const fixture = open(), component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: preview });
    await previewing;
    component.reviewed.set(true);
    const executing = component.executePlan();
    const request = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/execute` });
    const key = `auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`;
    const replacement = JSON.stringify({ templateRevisionId: revisionId, previewDigest: digest, idempotencyKey: templateId });
    sessionStorage.setItem(key, replacement);
    request.flush({ code: 'scope.denied', message: 'Access denied.' }, { status: 403, statusText: 'Forbidden' });
    await executing;
    expect(sessionStorage.getItem(key)).toBe(replacement);
    expect(component.canCleanReservation()).toBe(false);
    TestBed.inject(SessionService).current.set({ userId: firmId, firmId, generation: '2', staff: true });
    TestBed.tick();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`).flush(catalog);
    TestBed.tick();
    expect(component.canCleanReservation()).toBe(false);
    component.retryReservationCleanup();
    expect(sessionStorage.getItem(key)).toBe(replacement);
    http.expectNone(request => request.url.endsWith('/execute'));
  });

  it('does not clean an old-session marker when its scope refusal arrives in a new generation', async () => {
    const fixture = open(), component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` }).flush({ value: preview });
    await previewing;
    component.reviewed.set(true);
    const executing = component.executePlan();
    const request = http.expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/execute` });
    const key = `auditsphere:workflow-plan:${firmId}:${firmId}:1:${engagementId}`;
    const original = sessionStorage.getItem(key);
    TestBed.inject(SessionService).current.set({ userId: firmId, firmId, generation: '2', staff: true });
    TestBed.tick();
    http.expectOne(`/api/ui/engagements/${engagementId}/task-plan/templates`).flush(catalog);
    TestBed.tick();
    request.flush({ code: 'scope.denied', message: 'Access denied.' }, { status: 403, statusText: 'Forbidden' });
    await executing;
    expect(sessionStorage.getItem(key)).toBe(original);
    expect(component.priorRecovery()).toBe(true);
    expect(component.canCleanReservation()).toBe(false);
    http.expectNone(request => request.url.endsWith('/execute'));
  });

  it('does not offer execution when the server says the actor can only preview', async () => {
    const fixture = open();
    const component = fixture.componentInstance;
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    TestBed.inject(HttpTestingController).expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` })
      .flush({ value: { ...preview, canExecute: false } });
    await previewing;
    component.reviewed.set(true);
    expect(component.canExecute()).toBe(false);
    await component.executePlan();
    TestBed.inject(HttpTestingController).expectNone({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/execute` });
  });

  it('summarises unassigned, undated and dependent steps without changing the reviewed plan', async () => {
    const fixture = open();
    const component = fixture.componentInstance;
    component.selectTemplate(revisionId);
    const previewing = component.previewPlan();
    const fieldwork = { ...preview.steps[0], stepKey: 'fieldwork', templateStepId: alternateEvidenceId, title: 'Fieldwork', sortOrder: 2, dueDate: '2026-11-30',
      dependsOnStepKeys: ['planning'], missingEligibleAssignee: true, taskGroupName: 'Evidence review', requiredTask: false };
    TestBed.inject(HttpTestingController).expectOne({ method: 'POST', url: `/api/ui/engagements/${engagementId}/task-plan/preview` })
      .flush({ value: { ...preview, steps: [preview.steps[0], fieldwork] } });
    await previewing;
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.plan-summary')?.textContent?.replace(/\s+/g, ' ').trim())
      .toBe('2 step(s) · 1 without an eligible engagement assignee · 1 without a due date · 1 with prerequisites');
    expect(component.reviewed()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('Task group: Evidence review · Optional');
    expect(fixture.nativeElement.textContent).toContain('Task group: Ungrouped · Requiredness unavailable');
  });
});
