import { afterEach, describe, expect, it } from 'vitest';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SessionService } from '../../core/session';
import { WorkflowTaskCreate } from './workflow-task-create';

const engagement = '11111111-1111-4111-8111-111111111111';
const user = '22222222-2222-4222-8222-222222222222';
const task = '33333333-3333-4333-8333-333333333333';
const phases = [
  { index: 1, code: 'CLIENT_ONBOARDING', label: 'Client Onboarding' },
  { index: 2, code: 'AUDIT_PLANNING', label: 'Audit Planning' },
];
const url = `/api/ui/engagements/${engagement}/workflow/tasks`;

describe('manual engagement workflow-task creation', () => {
  afterEach(() => { sessionStorage.clear(); TestBed.resetTestingModule(); });

  function setup() {
    TestBed.configureTestingModule({ imports: [WorkflowTaskCreate], providers: [provideHttpClient(), provideHttpClientTesting()] });
    const session = TestBed.inject(SessionService);
    session.current.set({ userId: user, firmId: user, generation: '1', staff: true });
    const fixture = TestBed.createComponent(WorkflowTaskCreate);
    fixture.componentRef.setInput('engagementId', engagement);
    fixture.componentRef.setInput('phases', phases);
    fixture.detectChanges(); TestBed.tick();
    return { fixture, component: fixture.componentInstance, http: TestBed.inject(HttpTestingController) };
  }

  it('posts a MANUAL task payload using the server phase and refreshes after a confirmed create', async () => {
    const { fixture, component, http } = setup();
    component.title = 'Prepare lead schedule'; component.phase = 'AUDIT_PLANNING';
    component.ordinal = 3; component.dueDate = '2026-11-05'; component.plannedEffortMinutes = 90;
    let created = false; component.created.subscribe(() => { created = true; });
    const saving = component.submit();
    const post = http.expectOne((request) => request.url === url && request.method === 'POST');
    expect(post.request.body).toMatchObject({ title: 'Prepare lead schedule', phase: 'AUDIT_PLANNING', ordinal: 3,
      dueDate: '2026-11-05', plannedEffortMinutes: 90, assigneeUserId: null, mappingVersionId: null,
      fsliCode: null, assignedRole: null });
    expect(post.request.body.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/i);
    post.flush({ value: task }); await saving;
    expect(created).toBe(true); expect(component.pending()).toBeNull();
    expect(component.message()).toContain(task); expect(Object.keys(sessionStorage)).not.toContain(`auditsphere.workflow-task-create:${user}:${user}:${engagement}`);
    http.verify(); fixture.destroy();
  });

  it('retains and deliberately replays the exact payload and key after an uncertain outcome', async () => {
    const { fixture, component, http } = setup();
    component.title = 'Review planning evidence'; component.phase = 'AUDIT_PLANNING';
    const first = component.submit();
    const initial = http.expectOne((request) => request.url === url && request.method === 'POST');
    const requestBody = initial.request.body;
    initial.flush({}, { status: 503, statusText: 'Synthetic lost response' }); await first;
    expect(component.pending()?.request).toEqual(requestBody);
    const retry = component.retry();
    const replay = http.expectOne((request) => request.url === url && request.method === 'POST');
    expect(replay.request.body).toEqual(requestBody);
    replay.flush({ value: task }); await retry;
    expect(component.pending()).toBeNull(); http.verify(); fixture.destroy();
  });

  it('does not claim a server-denied creation succeeded or retain a refused request', async () => {
    const { fixture, component, http } = setup();
    component.title = 'Restricted task';
    const saving = component.submit();
    http.expectOne((request) => request.url === url && request.method === 'POST')
      .flush({ code: 'scope.denied', message: 'Access denied.' }, { status: 403, statusText: 'Forbidden' });
    await saving;
    expect(component.pending()).toBeNull(); expect(component.failed()).toBe(true);
    expect(component.message()).toBe('Access denied.'); http.verify(); fixture.destroy();
  });
});
