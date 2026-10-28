import { afterEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/session';
import { decodeApplicabilityHistory, decodeTaskApplicability, WorkflowTaskApplicabilityPanel } from './workflow-task-applicability';

const engagement = '11111111-1111-4111-8111-111111111111';
const task = '22222222-2222-4222-8222-222222222222';
const user = '33333333-3333-4333-8333-333333333333';
const source = '44444444-4444-4444-8444-444444444444';
const receipt = '55555555-5555-4555-8555-555555555555';
const proposalId = '66666666-6666-4666-8666-666666666666';
const decisionId = '77777777-7777-4777-8777-777777777777';
const hash = 'a'.repeat(64);
const basis = '{"TaskRevision":9007199254740993,"exact":"source"}';
const time = '2026-10-28T00:00:00.123456+00:00';
const url = `/api/ui/engagements/${engagement}/workflow/tasks/${task}/applicability`;
const workspace = { engagementId: engagement, taskId: task, taskRevision: '9007199254740993', latestProposalId: null,
  proposal: null, preview: null, canPropose: false, canApprove: false, canReturn: false, canRevoke: false };
const preview = { sourceRequestId: source, sourceReceiptId: receipt, basisHash: hash, basisJson: basis, blocker: null };
const proposal = { id: proposalId, version: '9007199254740993', actorUserId: source, actorName: 'Synthetic maker',
  reason: 'Source supports this conditional task decision.', occurredAt: time, sourceReceiptId: receipt, basisHash: hash,
  basisJson: basis, isLatest: true, basisCurrent: true, currentBasisJson: basis, blocker: null, decision: null };

describe('exact conditional applicability contracts', () => {
  it('preserves adjacent Int64 strings and opaque basis, refusing unsafe or inconsistent permission states', () => {
    for (const taskRevision of ['9007199254740992', '9007199254740993', '9223372036854775807'])
      expect(decodeTaskApplicability({ ...workspace, taskRevision, preview, canPropose: true }, '').taskRevision).toBe(taskRevision);
    expect(decodeTaskApplicability({ ...workspace, preview, canPropose: true }, '').preview?.basisJson).toBe(basis);
    for (const taskRevision of [9007199254740992, '09007199254740993', '9.007199254740993e15', '9223372036854775808', '0'])
      expect(() => decodeTaskApplicability({ ...workspace, taskRevision }, '')).toThrow();
    for (const change of [{ canPropose: true }, { canApprove: true }, { canRevoke: true },
      { proposal: { ...proposal, currentBasisJson: '{}' }, latestProposalId: proposalId },
      { proposal, latestProposalId: task }, { proposal, latestProposalId: proposalId, canRevoke: true }])
      expect(() => decodeTaskApplicability({ ...workspace, ...change }, '')).toThrow();
  });
});

describe('reviewed conditional applicability actions', () => {
  afterEach(() => { sessionStorage.clear(); TestBed.resetTestingModule(); });
  function setup(initial: object = workspace) {
    TestBed.configureTestingModule({ imports: [WorkflowTaskApplicabilityPanel], providers: [provideHttpClient(), provideHttpClientTesting()] });
    const session = TestBed.inject(SessionService); session.current.set({ userId: user, firmId: user, generation: '1', staff: true });
    const fixture = TestBed.createComponent(WorkflowTaskApplicabilityPanel);
    fixture.componentRef.setInput('engagementId', engagement); fixture.componentRef.setInput('taskId', task); fixture.componentRef.setInput('title', 'Synthetic conditional task');
    fixture.detectChanges(); TestBed.tick();
    const http = TestBed.inject(HttpTestingController); http.expectOne(url).flush(initial); TestBed.tick(); fixture.detectChanges();
    return { fixture, c: fixture.componentInstance, http, session };
  }
  function sourcePreview(c: WorkflowTaskApplicabilityPanel, http: HttpTestingController) {
    c.selectSource(source); TestBed.tick(); http.expectOne(`${url}?sourceRequestId=${source}`).flush({ ...workspace, preview, canPropose: true }); TestBed.tick();
  }

  it('requires fresh explicit assent, sends the exact server receipt/hash/revision once and refreshes after a matched receipt', async () => {
    const { fixture, c, http } = setup(); sourcePreview(c, http);
    c.reason = 'Synthetic reviewed reason.'; c.prepare('PROPOSE'); await c.submit(); http.expectNone(r => r.method === 'POST');
    expect(c.confirmed).toBe(false); c.confirmed = true;
    const saving = c.submit(); const posted = http.expectOne(`${url}/proposals`); const key = posted.request.body.idempotencyKey;
    expect(posted.request.body).toEqual({ sourceReceiptId: receipt, expectedProposalId: null, basisHash: hash,
      expectedTaskRevision: '9007199254740993', reason: c.reason, idempotencyKey: key, reviewed: true });
    await c.submit(); http.expectNone(r => r.method === 'POST');
    posted.flush({ value: { engagementId: engagement, taskId: task, idempotencyKey: key, proposalId, decisionId: null, version: '1' } }); await saving;
    http.expectOne(`${url}?sourceRequestId=${source}`).flush({ ...workspace, preview, canPropose: true, proposal, latestProposalId: proposalId });
    TestBed.tick(); expect(c.pending()).toBeNull(); expect(c.review()).toBeNull(); expect(c.confirmed).toBe(false);
    expect(c.message()).toContain('receipt confirmed'); http.verify(); fixture.destroy();
  });

  it('keeps uncertain or mismatched outcomes blocked and recovers only with GET without resending', async () => {
    const { fixture, c, http } = setup(); sourcePreview(c, http);
    c.reason = 'Synthetic reason.'; c.prepare('PROPOSE'); c.confirmed = true;
    const saving = c.submit(); const posted = http.expectOne(`${url}/proposals`); const key = posted.request.body.idempotencyKey;
    posted.flush({}, { status: 503, statusText: 'Lost reply' }); await saving; expect(c.pending()).toBe(key);
    await c.submit(); c.prepare('PROPOSE'); http.expectNone(r => r.method === 'POST');
    let recovering = c.recover(); http.expectOne(`${url}/receipts/${key}`).flush({ engagementId: task, taskId: task,
      idempotencyKey: key, proposalId, decisionId: null, version: '1' }); await recovering; expect(c.pending()).toBe(key);
    recovering = c.recover(); http.expectOne(`${url}/receipts/${key}`).flush({ engagementId: engagement, taskId: task,
      idempotencyKey: key, proposalId, decisionId: null, version: '1' }); await recovering;
    http.expectOne(`${url}?sourceRequestId=${source}`).flush({ ...workspace, preview, canPropose: true });
    expect(c.pending()).toBeNull(); http.expectNone(r => r.method === 'POST'); http.verify(); fixture.destroy();
  });

  it('clears stale assent on refresh and retains independent stale revocation without enabling approval', async () => {
    const approved = { ...proposal, basisCurrent: false, currentBasisJson: '{}', blocker: 'Source changed.',
      decision: { id: decisionId, version: '1', kind: 'APPROVE', actorUserId: task, actorName: 'Synthetic reviewer', reason: 'Review.', occurredAt: time } };
    const data = { ...workspace, proposal: approved, latestProposalId: proposalId, canRevoke: true };
    const { fixture, c, http } = setup(data);
    c.reason = 'Withdraw stale applicability.'; c.prepare('APPROVE'); expect(c.review()).toBeNull();
    c.prepare('REVOKE'); c.confirmed = true; c.refresh(); TestBed.tick(); expect(c.review()).toBeNull(); expect(c.confirmed).toBe(false);
    http.expectOne(url).flush(data); TestBed.tick(); c.prepare('REVOKE'); c.confirmed = true;
    const saving = c.submit(); const posted = http.expectOne(`${url}/decisions`);
    expect(posted.request.body).toMatchObject({ kind: 'REVOKE', proposalId, expectedDecisionId: decisionId, basisHash: hash, expectedTaskRevision: '9007199254740993' });
    posted.flush({ value: { engagementId: engagement, taskId: task, idempotencyKey: posted.request.body.idempotencyKey,
      proposalId, decisionId: source, version: '2' } }); await saving;
    http.expectOne(url).flush(workspace); http.verify(); fixture.destroy();
  });

  it('withholds late old-task and old-session recovery replies and preserves only the original scoped key', async () => {
    const { fixture, c, http, session } = setup(); sourcePreview(c, http);
    c.reason = 'Private reason.'; c.prepare('PROPOSE'); c.confirmed = true;
    const saving = c.submit(); const posted = http.expectOne(`${url}/proposals`); const key = posted.request.body.idempotencyKey;
    posted.flush({}, { status: 503, statusText: 'Lost reply' }); await saving;
    const lookup = c.recover(); const old = http.expectOne(`${url}/receipts/${key}`);
    fixture.componentRef.setInput('taskId', source); TestBed.tick(); fixture.detectChanges();
    http.expectOne(url.replace(task, source)).flush({ ...workspace, taskId: source }); TestBed.tick();
    c.reason = 'Current private draft.'; old.flush({ engagementId: engagement, taskId: task, idempotencyKey: key, proposalId, decisionId: null, version: '1' }); await lookup;
    expect(c.reason).toBe('Current private draft.'); expect(c.pending()).toBeNull(); expect(c.message()).toBe('');
    expect(sessionStorage.getItem(`auditsphere.workflow-applicability:${user}:${user}:${engagement}:${task}`)).toBe(key);
    fixture.componentRef.setInput('taskId', task); TestBed.tick(); http.expectOne(url).flush(workspace); TestBed.tick(); expect(c.pending()).toBe(key);
    const lookupSession = c.recover(); const oldSession = http.expectOne(`${url}/receipts/${key}`);
    session.clear(); TestBed.tick(); fixture.detectChanges(); oldSession.flush({}, { status: 503, statusText: 'Old session' }); await lookupSession;
    expect(c.workspace.data()).toBeNull(); expect(c.reason).toBe(''); expect(c.review()).toBeNull(); expect(c.message()).toBe('');
    http.verify(); fixture.destroy();
  });

  it('keeps bounded history cursor microseconds exact and refreshes rather than accumulating old pages', () => {
    const { fixture, c, http } = setup();
    const items = Array.from({ length: 25 }, (_, i) => ({ id: `88888888-8888-4888-8888-${String(i).padStart(12, '0')}`,
      recordType: 'PROPOSAL', kind: 'PROPOSE', proposalId, version: String(25 - i), actorUserId: source, actorName: 'Synthetic maker',
      reason: 'Historical source.', basisHash: hash, occurredAt: time }));
    const last = items.at(-1)!; const nextCursor = { occurredAt: time, recordType: 'PROPOSAL', id: last.id };
    const page = { engagementId: engagement, taskId: task, items, hasMore: true, nextCursor };
    expect(() => decodeApplicabilityHistory({ ...page, items: [...items, items[0]] }, '')).toThrow();
    c.openHistory(); TestBed.tick(); http.expectOne(`${url}/history`).flush(page); TestBed.tick(); c.nextHistory(); TestBed.tick();
    const params = new URLSearchParams({ afterOccurredAt: time, afterType: 'PROPOSAL', afterId: last.id });
    http.expectOne(`${url}/history?${params}`).flush({ ...page, items: [], hasMore: false, nextCursor: null }); TestBed.tick();
    c.firstHistory(); TestBed.tick(); http.expectOne(`${url}/history`).flush(page); TestBed.tick(); expect(c.history.data()?.items).toHaveLength(25);
    c.showProposal(proposalId); TestBed.tick(); http.expectOne(`${url}?proposalId=${proposalId}`).flush({ ...workspace, proposal, latestProposalId: proposalId });
    TestBed.tick(); expect(c.confirmed).toBe(false); http.verify(); fixture.destroy();
  });
});
