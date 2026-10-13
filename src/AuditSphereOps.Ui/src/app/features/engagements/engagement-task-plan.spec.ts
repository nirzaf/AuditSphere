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
  templateUpdateNotice: null, canExecute: true,
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
      createdAt: '2026-10-12T09:15:00Z', taskIds: [templateId],
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
