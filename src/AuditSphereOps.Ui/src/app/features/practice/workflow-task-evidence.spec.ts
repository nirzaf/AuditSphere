import { afterEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { decodeTaskEvidence, WorkflowTaskEvidencePanel } from './workflow-task-evidence';

const engagement = '11111111-1111-4111-8111-111111111111';
const task = '22222222-2222-4222-8222-222222222222';
const user = '33333333-3333-4333-8333-333333333333';
const rule = { id: user, kind: 'CURRENT_MATERIALITY_APPROVED', sourceId: null, state: 'REQUIRED', version: 1,
  satisfied: false, sourceState: 'MISSING', message: 'Approve current materiality.', sourceRevision: null,
  inputGeneration: null, reason: 'Current planning is required.', occurredAt: '2026-10-13T12:00:00Z' };
const workspace = { engagementId: engagement, taskId: task, taskRevision: 1, canManage: true, rules: [] as unknown[] };
const url = `/api/ui/engagements/${engagement}/workflow/tasks/${task}/evidence`;

describe('task evidence contract', () => {
  it('retains exact scope, source revision and current planning generation', () => {
    const decoded = decodeTaskEvidence({ ...workspace, rules: [{ ...rule, kind: 'CURRENT_PROCEDURE_REVIEWED', sourceId: task,
      sourceRevision: 3, inputGeneration: 2, sourceState: 'STALE_GENERATION' }] }, 'evidence');
    expect(decoded.rules[0].sourceRevision).toBe(3); expect(decoded.rules[0].inputGeneration).toBe(2);
    expect(decoded.rules[0].satisfied).toBe(false);
  });
  it('rejects unsupported, inconsistent, duplicate and over-bound prerequisites', () => {
    for (const item of [{ ...rule, kind: 'MANUAL_SATISFIED' }, { ...rule, sourceId: task },
      { ...rule, satisfied: true }, { ...rule, version: 0 }, { ...rule, reason: '' },
      { ...rule, kind: 'CURRENT_PROCEDURE_REVIEWED', sourceId: null }, { ...rule, inputGeneration: -1 }])
      expect(() => decodeTaskEvidence({ ...workspace, rules: [item] }, 'evidence')).toThrow();
    expect(() => decodeTaskEvidence({ ...workspace, rules: [rule, rule] }, 'evidence')).toThrow();
    expect(() => decodeTaskEvidence({ ...workspace, rules: Array.from({ length: 9 }, () => rule) }, 'evidence')).toThrow();
  });
});

describe('reviewed task evidence changes', () => {
  afterEach(() => { sessionStorage.clear(); TestBed.resetTestingModule(); });
  function setup(canManage = true) {
    TestBed.configureTestingModule({ imports: [WorkflowTaskEvidencePanel], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const session = TestBed.inject(SessionService); session.current.set({ userId: user, firmId: user, generation: '1', staff: true });
    const fixture = TestBed.createComponent(WorkflowTaskEvidencePanel);
    fixture.componentRef.setInput('engagementId', engagement); fixture.componentRef.setInput('taskId', task); fixture.componentRef.setInput('title', 'Synthetic coordination task');
    fixture.detectChanges(); TestBed.tick();
    const http = TestBed.inject(HttpTestingController); http.expectOne(url).flush({ ...workspace, canManage }); TestBed.tick(); fixture.detectChanges();
    return { fixture, component: fixture.componentInstance, http, session };
  }

  it('requires current manager authority and exact review assent before one POST', async () => {
    const { component: c, http, fixture } = setup();
    c.reason = 'Require the current independently approved materiality.';
    c.prepare('CURRENT_MATERIALITY_APPROVED', null, true); await c.submit(); http.expectNone(r => r.method === 'POST');
    c.confirmed = true; const saving = c.submit();
    const posted = http.expectOne(r => r.url === url && r.method === 'POST'); expect(posted.request.body.expectedRevision).toBe(1);
    const key = posted.request.body.idempotencyKey;
    posted.flush({ value: { engagementId: engagement, taskId: task, idempotencyKey: key, ruleRevisionId: user, taskRevision: 2 } });
    await saving; http.expectOne(url).flush({ ...workspace, taskRevision: 2, rules: [rule] });
    expect(c.pending()).toBeNull(); expect(c.message()).toBe('Prerequisite change confirmed.');
    http.verify(); fixture.destroy();
  });

  it('recovers a lost reply using GET without resending the change', async () => {
    const { component: c, http, fixture } = setup();
    c.reason = 'Wait for current materiality.'; c.prepare('CURRENT_MATERIALITY_APPROVED', null, true); c.confirmed = true;
    const saving = c.submit(); const posted = http.expectOne(r => r.method === 'POST'); const key = posted.request.body.idempotencyKey;
    posted.flush({}, { status: 503, statusText: 'Synthetic lost reply' }); await saving;
    expect(c.pending()).toBe(key); await c.submit(); http.expectNone(r => r.method === 'POST');
    const recovering = c.recover(); http.expectOne(`${url}/receipts/${key}`).flush({ engagementId: engagement, taskId: task, idempotencyKey: key, ruleRevisionId: user, taskRevision: 2 });
    await recovering; http.expectOne(url).flush({ ...workspace, taskRevision: 2, rules: [rule] });
    expect(c.pending()).toBeNull(); http.expectNone(r => r.method === 'POST'); http.verify(); fixture.destroy();
  });

  it('keeps an unmatched receipt uncertain and clears private draft state after session invalidation', async () => {
    const { component: c, http, fixture, session } = setup();
    c.reason = 'Private operational routing reason'; c.prepare('CURRENT_MATERIALITY_APPROVED', null, true); c.confirmed = true;
    const saving = c.submit(); const posted = http.expectOne(r => r.method === 'POST');
    posted.flush({ value: { engagementId: task, taskId: task, idempotencyKey: posted.request.body.idempotencyKey, ruleRevisionId: user, taskRevision: 2 } });
    await saving; expect(c.pending()).not.toBeNull(); expect(c.failed()).toBe(true);

    const key = c.pending()!;
    const recoveringOldTask = c.recover();
    const oldTaskLookup = http.expectOne(`${url}/receipts/${key}`);
    fixture.componentRef.setInput('taskId', user); TestBed.tick(); fixture.detectChanges();
    http.expectOne(url.replace(task, user)).flush({ ...workspace, taskId: user });
    TestBed.tick(); fixture.detectChanges();
    c.reason = 'Current task draft';
    oldTaskLookup.flush({}, { status: 503, statusText: 'Late old-task lookup failure' });
    await recoveringOldTask;
    expect(c.failed()).toBe(false); expect(c.message()).toBe('');
    expect(c.reason).toBe('Current task draft'); expect(c.pending()).toBeNull();
    expect(sessionStorage.getItem(`auditsphere.workflow-evidence:${user}:${user}:${engagement}:${task}`)).toBe(key);

    fixture.componentRef.setInput('taskId', task); TestBed.tick(); fixture.detectChanges();
    http.expectOne(url).flush(workspace); TestBed.tick(); fixture.detectChanges();
    expect(c.pending()).toBe(key);
    const recoveringOldSession = c.recover();
    const oldSessionLookup = http.expectOne(`${url}/receipts/${key}`);
    c.reason = 'Private operational routing reason';
    session.clear(); TestBed.tick(); fixture.detectChanges();
    oldSessionLookup.flush({}, { status: 503, statusText: 'Late old-session lookup failure' });
    await recoveringOldSession;
    expect(c.failed()).toBe(false); expect(c.message()).toBe('');
    expect(c.workspace.data()).toBeNull(); expect(c.review()).toBeNull(); expect(c.reason).toBe('');
    expect(fixture.nativeElement.textContent).not.toContain('Private operational routing reason');
    http.expectNone(r => r.method === 'POST'); http.verify({ ignoreCancelled: true }); fixture.destroy();
  });

  it('does not expose editing to a staff reader or retain data from another task', () => {
    const { component: c, fixture, http } = setup(false);
    c.reason = 'Not authorized'; c.prepare('CURRENT_MATERIALITY_APPROVED', null, true);
    expect(c.review()).toBeNull(); expect(fixture.nativeElement.querySelector('form')).toBeNull();
    c.workspace.reload(); http.expectOne(url).flush({ ...workspace, taskId: user }); TestBed.tick(); fixture.detectChanges();
    expect(c.workspace.data()).toBeNull(); expect(c.workspace.error()).toBeTruthy(); http.verify(); fixture.destroy();
  });
});
