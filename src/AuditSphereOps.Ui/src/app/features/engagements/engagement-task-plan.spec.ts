import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/session';
import { EngagementTaskPlan } from './engagement-task-plan';
import { describe, expect, it } from 'vitest';

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
});
