import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { afterEach, describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { decodeReminderRuleCatalog, decodeReminderRuleRevision, WorkflowReminderRules } from './reminder-rules';

const firm = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const user = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const author = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const rule = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
const revisionId = 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee';
const digest = 'a'.repeat(64);
const definition = {
  name: 'Task due follow-up', sourceKind: 'TASK_DUE', recipientKind: 'RESPONSIBLE_STAFF',
  offsetDays: -2, repeatEveryDays: 3, maxScheduledWindowCount: 3,
  timeZoneId: 'Asia/Qatar', quietHoursStart: '21:00', quietHoursEnd: '08:00',
  effectiveFromUtc: '2026-10-19T00:00:00Z',
} as const;
const revision = (overrides: Partial<ReturnType<typeof decodeReminderRuleRevision>> = {}): ReturnType<typeof decodeReminderRuleRevision> => ({
  ruleId: rule, revisionId, version: 1, definition, status: 'DRAFT', contentDigest: digest,
  revision: 1, ruleRevision: 1, createdByUserId: author, lastEditedByUserId: author,
  reviewedByUserId: null, publishedByUserId: null, createdAt: '2026-10-19T00:00:00Z',
  updatedAt: '2026-10-19T00:00:00Z', reviewedAt: null, publishedAt: null, suspended: false,
  canEdit: false, canReview: true, canPublish: false, canSuspend: false, canResume: false,
  blocker: null, ...overrides,
});
const catalogPage = (ruleId = rule, revId: string | null = revisionId) => ({
  executionEnabled: false, canManage: true, hasMore: false, nextCursor: null,
  items: [{ ruleId, revisionId: revId, version: revId ? 1 : null, name: revId ? definition.name : null,
    status: revId ? 'DRAFT' : null, suspended: false, revision: 1, createdAt: '2026-10-19T00:00:00Z' }],
});
const emptyHistory = { items: [], hasMore: false, nextAfterId: null };
const emptyExecutions = { items: [], hasMore: false, nextCursor: null, canOpenOperations: false };
const rootUrl = '/api/ui/practice/reminder-rules?pageSize=25';
const revisionUrl = `/api/ui/practice/reminder-rules/${rule}/revisions/${revisionId}`;
const historyUrl = `/api/ui/practice/reminder-rules/${rule}/history?pageSize=25`;
const executionsUrl = `/api/ui/practice/reminder-rules/${rule}/executions?pageSize=25`;
const yieldToAsyncWork = () => new Promise<void>(resolve => setTimeout(resolve, 0));

describe('workflow reminder rule contract', () => {
  it('requires nullable contract members and preserves server-projected authority', () => {
    const decodedCatalog = decodeReminderRuleCatalog(catalogPage(), 'catalog');
    expect(decodedCatalog.canManage).toBe(true);
    expect(decodedCatalog.items[0].lastEvaluatedAt).toBeNull();
    expect(decodedCatalog.items[0].lastEvaluationCode).toBeNull();
    expect(decodeReminderRuleCatalog({ ...catalogPage(), items: [{ ...catalogPage().items[0],
      lastEvaluatedAt: '2026-10-19T01:00:00Z', lastEvaluationCode: 'NO_DUE_SOURCES' }] }, 'catalog').items[0])
      .toMatchObject({ lastEvaluatedAt: '2026-10-19T01:00:00Z', lastEvaluationCode: 'NO_DUE_SOURCES' });
    expect(decodeReminderRuleRevision(revision(), 'revision').canReview).toBe(true);
    const legacyDefinition = Object.fromEntries(Object.entries(definition)
      .filter(([key]) => key !== 'maxScheduledWindowCount'));
    expect(decodeReminderRuleRevision({ ...revision(), definition: legacyDefinition }, 'legacy').definition)
      .toMatchObject({ repeatEveryDays: 3, maxScheduledWindowCount: null });
    expect(() => decodeReminderRuleRevision({ ...revision(), blocker: undefined }, 'revision')).toThrow();
    const missingCursor = { ...catalogPage() } as Record<string, unknown>;
    delete missingCursor['nextCursor'];
    expect(() => decodeReminderRuleCatalog(missingCursor, 'catalog')).toThrow();
  });
});

describe('workflow reminder rule administration', () => {
  afterEach(() => { TestBed.resetTestingModule(); });

  async function setup() {
    TestBed.configureTestingModule({ imports: [WorkflowReminderRules], providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
    ] });
    TestBed.inject(SessionService).current.set({ userId: user, firmId: firm, generation: '1', staff: true });
    const fixture = TestBed.createComponent(WorkflowReminderRules);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(rootUrl).flush(catalogPage());
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, http, component: fixture.componentInstance };
  }

  it('starts with no timing policy defaults and saves the explicit definition as a draft', async () => {
    const { fixture, http, component } = await setup();
    component.startNewRule();
    const requestKey = component.draft.requestKey;
    expect(requestKey).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(component.draft.definition).toMatchObject({ name: '', sourceKind: '', recipientKind: '', offsetDays: null,
      repeatEveryDays: null, maxScheduledWindowCount: null,
      timeZoneId: '', quietHoursStart: '', quietHoursEnd: '', effectiveFromUtc: '' });
    component.draft.definition = { ...definition, maxScheduledWindowCount: null };
    await component.saveDraft();
    expect(component.message()).toContain('maximum of 1 to 100 scheduled windows');
    http.expectNone('/api/ui/practice/reminder-rules/drafts');
    component.draft.definition = { ...definition };
    const saving = component.saveDraft();
    const request = http.expectOne('/api/ui/practice/reminder-rules/drafts');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ ruleId: null, definition, expectedRevision: null, requestKey });
    const createdRuleId = '99999999-9999-4999-8999-999999999999';
    const saved = revision({ ruleId: createdRuleId, definition: { ...definition, effectiveFromUtc: '2026-10-19T00:00:00+00:00' }, createdByUserId: user, lastEditedByUserId: user, canEdit: true, canReview: false });
    request.flush({ value: saved });
    await saving;
    http.expectOne(rootUrl).flush(catalogPage(createdRuleId));
    http.expectOne(`/api/ui/practice/reminder-rules/${createdRuleId}/history?pageSize=25`).flush(emptyHistory);
    http.expectOne(`/api/ui/practice/reminder-rules/${createdRuleId}/executions?pageSize=25`).flush(emptyExecutions);
    expect(component.revision()?.status).toBe('DRAFT');
    expect(component.draftMode()).toBeNull();
    component.startBlankRevision(revision({ ruleId: createdRuleId, status: 'PUBLISHED', ruleRevision: 2 }));
    expect(component.draftMode()).toBe('revision');
    expect(component.draft.expectedRevision).toBe(2);
    expect(component.draft.requestKey).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(component.draft.requestKey).not.toBe(requestKey);
    fixture.destroy();
    http.verify();
  });

  it('checks the exact creation receipt and retries only the original body and request key', async () => {
    const { fixture, http, component } = await setup();
    component.startNewRule();
    const requestKey = component.draft.requestKey!;
    component.draft.definition = { ...definition };
    const saving = component.saveDraft();
    const post = http.expectOne('/api/ui/practice/reminder-rules/drafts');
    const originalBody = { ruleId: null, definition, expectedRevision: null, requestKey };
    expect(post.request.body).toEqual(originalBody);
    post.flush({}, { status: 503, statusText: 'Lost response' });
    await yieldToAsyncWork();
    http.expectOne(`/api/ui/practice/reminder-rules/drafts/${requestKey}/receipt`)
      .flush({}, { status: 404, statusText: 'Not Found' });
    await saving;
    expect(component.uncertain()).toBe(true);
    expect(component.receiptMissing()).toBe(true);
    expect(component.canRetryExactDraftSave()).toBe(true);
    const retrying = component.retryExactDraftSave();
    const retry = http.expectOne('/api/ui/practice/reminder-rules/drafts');
    expect(retry.request.body).toEqual(originalBody);
    expect(retry.request.body.requestKey).toBe(requestKey);
    const createdRuleId = '99999999-9999-4999-8999-999999999999';
    retry.flush({ value: revision({ ruleId: createdRuleId, createdByUserId: user, lastEditedByUserId: user, canEdit: true, canReview: false }) });
    await retrying;
    http.expectOne(rootUrl).flush(catalogPage(createdRuleId));
    http.expectOne(`/api/ui/practice/reminder-rules/${createdRuleId}/history?pageSize=25`).flush(emptyHistory);
    http.expectOne(`/api/ui/practice/reminder-rules/${createdRuleId}/executions?pageSize=25`).flush(emptyExecutions);
    await fixture.whenStable();
    expect(component.uncertain()).toBe(false);
    expect(component.receiptMissing()).toBe(false);
    fixture.destroy();
    http.verify();
  });

  it('uses only server review authority and confirms an uncertain decision by exact history readback', async () => {
    const { fixture, http, component } = await setup();
    component.selectRule(decodeReminderRuleCatalog(catalogPage(), 'catalog').items[0]);
    http.expectOne(revisionUrl).flush(revision());
    http.expectOne(historyUrl).flush(emptyHistory);
    http.expectOne(executionsUrl).flush(emptyExecutions);
    await fixture.whenStable();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Review this revision');
    component.rationale = 'Reviewed the exact internal reminder rule';
    component.assent = true;
    component.review(component.revision()!);
    const post = http.expectOne(`/api/ui/practice/reminder-rules/${rule}/revisions/${revisionId}/review`);
    expect(post.request.body).toEqual({ expectedRevision: 1, contentDigest: digest, rationale: component.rationale });
    post.flush({}, { status: 503, statusText: 'Lost response' });
    await yieldToAsyncWork();
    http.expectOne(revisionUrl).flush(revision({ status: 'REVIEWED', reviewedByUserId: user, reviewedAt: '2026-10-19T00:02:00Z' }));
    http.expectOne(`/api/ui/practice/reminder-rules/${rule}/history?pageSize=100`).flush({
      items: [{ id: 'ffffffff-ffff-4fff-8fff-ffffffffffff', ruleRevisionId: revisionId, kind: 'REVIEWED',
        contentDigest: digest, rationale: component.rationale, actorUserId: user, occurredAt: '2026-10-19T00:02:00Z' }],
      hasMore: false, nextAfterId: null,
    });
    await yieldToAsyncWork();
    http.expectOne(rootUrl).flush(catalogPage());
    http.expectOne(historyUrl).flush(emptyHistory);
    http.expectOne(executionsUrl).flush(emptyExecutions);
    await fixture.whenStable();
    expect(component.uncertain()).toBe(false);
    expect(component.revision()?.status).toBe('REVIEWED');
    http.expectNone(request => request.method === 'POST');
    fixture.destroy();
    http.verify();
  });
});
