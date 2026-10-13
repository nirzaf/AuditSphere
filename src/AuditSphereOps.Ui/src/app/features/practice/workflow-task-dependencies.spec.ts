import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/session';
import { decodeTaskDependencies, WorkflowTaskDependenciesPanel } from './workflow-task-dependencies';

const engagement = '00000000-0000-4000-8000-000000000001', task = '00000000-0000-4000-8000-000000000002';
const predecessor = '00000000-0000-4000-8000-000000000003', user = '00000000-0000-4000-8000-000000000004';
const state = { engagementId: engagement, taskId: task, taskRevision: 1, canManage: true,
  tasks: [{ taskId: task, bindingId: task, title: 'Successor', status: 'OPEN', revision: 1 },
    { taskId: predecessor, bindingId: predecessor, title: 'Predecessor', status: 'OPEN', revision: 3 }], references: [] as unknown[] };
const url = `/api/ui/engagements/${engagement}/workflow/tasks/${task}/dependencies`;

describe('reviewed task ordering', () => {
  afterEach(() => { sessionStorage.clear(); TestBed.resetTestingModule(); });
  function setup(canManage = true) {
    TestBed.configureTestingModule({ imports: [WorkflowTaskDependenciesPanel], providers: [provideHttpClient(), provideHttpClientTesting()] });
    const session = TestBed.inject(SessionService); session.current.set({ userId: user, firmId: user, generation: '1', staff: true });
    const fixture = TestBed.createComponent(WorkflowTaskDependenciesPanel);
    fixture.componentRef.setInput('engagementId', engagement); fixture.componentRef.setInput('taskId', task); fixture.componentRef.setInput('title', 'Successor');
    fixture.detectChanges(); TestBed.tick(); const http = TestBed.inject(HttpTestingController);
    http.expectOne(url).flush({ ...state, canManage }); TestBed.tick(); fixture.detectChanges();
    return { fixture, c: fixture.componentInstance, http, session };
  }
  it('rejects missing, duplicate, self, cross-graph and revision-mismatched references', () => {
    for (const bad of [ { ...state, taskRevision: 2 }, { ...state, tasks: [...state.tasks, state.tasks[0]] },
      { ...state, references: [{ predecessorTaskId: task, required: true, version: 1, reason: 'Self' }] },
      { ...state, references: [{ predecessorTaskId: user, required: true, version: 1, reason: 'Foreign' }] } ])
      expect(() => decodeTaskDependencies(bad, 'graph')).toThrow();
    expect(decodeTaskDependencies(state, 'graph').tasks).toHaveLength(2);
  });
  it('binds both revisions and exact assent and posts only the reviewed payload', async () => {
    const { c, http, fixture } = setup(); c.reason = 'Reviewed ordering'; c.prepare(predecessor, true);
    await c.submit(); http.expectNone(r => r.method === 'POST'); c.confirmed = true; const saving = c.submit();
    const post = http.expectOne(r => r.method === 'POST'); expect(post.request.body.expectedPredecessorRevision).toBe(3);
    expect(post.request.body.predecessorTitle).toBeUndefined(); expect(post.request.body.reason).toBe('Reviewed ordering');
    const key = post.request.body.idempotencyKey;
    post.flush({ value: { engagementId: engagement, taskId: task, idempotencyKey: key, changeId: user, taskRevision: 2 } });
    await saving; http.expectOne(url).flush({ ...state, taskRevision: 2, tasks: [{ ...state.tasks[0], revision: 2 }, state.tasks[1]] });
    expect(c.pending()).toBeNull(); http.verify(); fixture.destroy();
  });
  it('recovers uncertain results with GET only and clears private drafts after revocation', async () => {
    const { c, http, fixture, session } = setup(); c.reason = 'Private routing reason'; c.prepare(predecessor, true); c.confirmed = true;
    const saving = c.submit(); const post = http.expectOne(r => r.method === 'POST'); const key = post.request.body.idempotencyKey;
    post.flush({}, { status: 503, statusText: 'Lost reply' }); await saving; expect(c.pending()).toBe(key);
    await c.submit(); http.expectNone(r => r.method === 'POST'); const recovering = c.recover();
    http.expectOne(`${url}/receipts/${key}`).flush({ engagementId: engagement, taskId: task, idempotencyKey: key, changeId: user, taskRevision: 2 });
    await recovering; http.expectOne(url).flush({ ...state, taskRevision: 2, tasks: [{ ...state.tasks[0], revision: 2 }, state.tasks[1]] });
    session.clear(); TestBed.tick(); fixture.detectChanges(); expect(c.workspace.data()).toBeNull(); expect(c.review()).toBeNull(); expect(c.reason).toBe('');
    http.verify({ ignoreCancelled: true }); fixture.destroy();
  });
  it('does not expose edits or submit for read-only staff', async () => {
    const { c, http, fixture } = setup(false); c.reason = 'No manager scope'; c.prepare(predecessor, true); c.confirmed = true; await c.submit();
    expect(c.review()).toBeNull(); expect(fixture.nativeElement.textContent).not.toContain('Review task ordering'); http.expectNone(r => r.method === 'POST');
    http.verify(); fixture.destroy();
  });
});
