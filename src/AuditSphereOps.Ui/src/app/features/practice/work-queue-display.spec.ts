import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { provideWorkspaceAvailabilityForTests } from '../../core/workspace-availability.testing';
import { WorkflowWorkQueue } from './work-queue';

const user = '11111111-1111-4111-8111-111111111111';
const engagement = '22222222-2222-4222-8222-222222222222';
const task = (n: number, taskStatus: string, dueDate: string | null, canComplete = false) => ({
  taskId: `33333333-3333-4333-8333-0000000000${String(n).padStart(2, '0')}`,
  clientId: user, clientName: 'Synthetic client', engagementId: engagement, engagementServiceRoute: 'Audit',
  engagementPeriod: '2026', title: `Queue task ${n}`, phase: 'AUDIT_FIELDWORK', ordinal: n, taskStatus,
  assigneeUserId: null, assigneeDisplayName: null, dueDate, mappingVersionId: null as string | null, fsliCode: null,
  onHold: false, canCancel: false, unmetTaskDependencies: 0, unmetEvidencePrerequisites: 0, canComplete, canChangeDueDate: false, canAssign: false, revision: 1,
  sourceKey: 'test:work-queue-display', templateRevisionId: null, templateStepId: null,
  pbcRequestId: null, pbcState: null, reviewState: null, reviewComment: null,
});
const queue = {
  view: 'MY_TASKS', hasMore: false, nextCursor: null,
  summary: null,
  tasks: [task(1, 'OPEN', '2026-10-11', true), task(2, 'IN_PROGRESS', '2026-10-15'), task(3, 'COMPLETED', null)],
};

describe('work queue display modes', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [WorkflowWorkQueue],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideWorkspaceAvailabilityForTests()],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: user, firmId: user, generation: '1', staff: true });
  });
  afterEach(() => http.verify());

  function setup(rows = queue) {
    const fixture = TestBed.createComponent(WorkflowWorkQueue);
    fixture.detectChanges();
    http.expectOne((r) => r.url.startsWith('/api/ui/work/tasks?')).flush(rows);
    http.expectOne((r) => r.url.startsWith('/api/ui/work/reviews?')).flush({ reviews: [], hasMore: false, nextCursor: null });
    TestBed.tick();
    fixture.detectChanges();
    return fixture;
  }
  const titles = (root: HTMLElement, selector: string) =>
    [...root.querySelectorAll(selector)].map((x) => x.textContent ?? '').filter((x) => x.includes('Queue task'));

  it('shows the same tasks as a list, a board and a calendar without another request', () => {
    const fixture = setup();
    const root = fixture.nativeElement as HTMLElement;
    expect(titles(root, 'tbody tr')).toHaveLength(3);

    fixture.componentInstance.display.set('BOARD');
    fixture.detectChanges();
    expect(root.querySelector('table caption')?.textContent ?? '').not.toContain('workflow tasks');
    const columns = [...root.querySelectorAll('.board-column')];
    expect(columns.map((c) => c.querySelector('h2')?.textContent?.trim().replace(/\s+/g, ' '))).toEqual(['Open (1)', 'In progress (1)', 'Completed (1)']);
    expect(titles(root, '.task-card')).toHaveLength(3);
    expect(root.textContent).toContain('cards cannot be dragged');
    // Activity is read-only; the only state-changing card action is server-authorized task completion.
    const actions = [...root.querySelectorAll('.task-card button')].map((b) => b.textContent?.trim());
    expect(actions.filter(x => x === 'View activity')).toHaveLength(3);
    expect(actions.filter(x => !['Review prerequisites', 'Review task ordering', 'View activity'].includes(x ?? ''))).toEqual(['Complete task']);
    expect(actions.filter(x => x === 'Review prerequisites')).toHaveLength(3);
    expect(actions.filter(x => x === 'Review task ordering')).toHaveLength(3);

    fixture.componentInstance.display.set('CALENDAR');
    fixture.detectChanges();
    expect(titles(root, '.task-card')).toHaveLength(3);
    expect(root.querySelector('.calendar-week h2')?.textContent).toContain('Week of 2026-10-11');
    expect(root.querySelectorAll('.calendar-day.weekend')).toHaveLength(2);
    expect(root.textContent).toContain('No due date');
    fixture.destroy();
  });

  it('discloses lineage and secondary actions without hiding blocked task facts or sending commands', () => {
    const rows = { ...queue, tasks: [{ ...task(1, 'OPEN', null, true),
      unmetTaskDependencies: 1, mappingVersionId: engagement }] };
    const fixture = setup(rows);
    const root = fixture.nativeElement as HTMLElement;
    for (const mode of ['LIST', 'BOARD', 'CALENDAR', 'GROUPED'] as const) {
      fixture.componentInstance.display.set(mode); fixture.detectChanges();
      expect(root.textContent).toContain('Queue task 1');
      expect(root.textContent).toContain('Synthetic client');
      expect(root.textContent).toContain('Unassigned');
      expect(root.textContent?.toLowerCase()).toContain('no due date');
      expect(root.textContent).toContain('task prerequisite(s) incomplete');
      const details = root.querySelector('details.task-source-details') as HTMLDetailsElement;
      expect(details.open).toBe(false);
      expect(details.querySelector('summary')?.textContent).toBe('Task source details');
      const summary = details.querySelector('summary')!;
      summary.click(); fixture.detectChanges();
      expect(details.open).toBe(true);
      expect(details.textContent).toContain('test:work-queue-display');
      expect(details.textContent).toContain(engagement);
      summary.click(); expect(details.open).toBe(false);
      const actions = root.querySelector('details.task-more-actions') as HTMLDetailsElement;
      expect(actions.open).toBe(false);
      expect(actions.textContent).toContain('Review task ordering');
      const complete = [...root.querySelectorAll('button')].find((button) => button.textContent?.trim() === 'Complete task')!;
      expect(complete.disabled).toBe(true);
      expect(complete.closest('details')).toBeNull();
      http.expectNone((request) => request.method !== 'GET');
    }
    fixture.destroy();
  });

  it('keeps held completion disabled and eligible completion enabled from current server flags', () => {
    const fixture = setup({ ...queue, tasks: [{ ...task(1, 'OPEN', null, true), onHold: true }, task(2, 'OPEN', null, true)] });
    fixture.componentInstance.display.set('BOARD'); fixture.detectChanges();
    const buttons = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .filter(button => button.textContent?.trim() === 'Complete task');
    expect(buttons.map(button => button.disabled)).toEqual([true, false]);
    http.expectNone(request => request.method !== 'GET');
    fixture.destroy();
  });

  it('opens the replacement disclosure before restoring recovery focus after a display change', () => {
    const fixture = setup({ ...queue, tasks: [{ ...task(1, 'OPEN', null), canCancel: true }] });
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLDetailsElement>('details.task-more-actions')!.open = true;
    const trigger = root.querySelector<HTMLButtonElement>('button[data-cancel-task]')!;
    trigger.focus(); trigger.click(); fixture.detectChanges(); TestBed.tick();
    fixture.componentInstance.display.set('BOARD'); fixture.detectChanges(); TestBed.tick();
    fixture.componentInstance.closeCancellation(); fixture.detectChanges(); TestBed.tick();
    const replacement = root.querySelector<HTMLButtonElement>('button[data-cancel-task]')!;
    expect(replacement.closest<HTMLDetailsElement>('details')!.open).toBe(true);
    expect(document.activeElement).toBe(replacement);
    http.expectNone(request => request.method !== 'GET');
    fixture.destroy();
  });

  it('clears availability after a server denial instead of presenting an empty queue as success', () => {
    const fixture = TestBed.createComponent(WorkflowWorkQueue);
    fixture.detectChanges();
    const unavailable = {
      code: 'scope.denied',
      message: 'Workflow task experience is not enabled on this server.',
    };
    http.expectOne((r) => r.url.startsWith('/api/ui/work/tasks?'))
      .flush(unavailable, { status: 403, statusText: 'Forbidden' });
    http.expectOne('/api/ui/work/reviews?pageSize=50')
      .flush(unavailable, { status: 403, statusText: 'Forbidden' });
    TestBed.tick();
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Work availability is temporarily unavailable');
    expect(root.textContent).not.toContain('My tasks: 0');
    expect(root.querySelectorAll('.summary-card')).toHaveLength(0);
    expect(root.querySelectorAll('.task-card')).toHaveLength(0);
    fixture.destroy();
  });
});
