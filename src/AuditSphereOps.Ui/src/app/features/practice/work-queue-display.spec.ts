import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { WorkflowWorkQueue } from './work-queue';

const user = '11111111-1111-4111-8111-111111111111';
const engagement = '22222222-2222-4222-8222-222222222222';
const task = (n: number, taskStatus: string, dueDate: string | null, canComplete = false) => ({
  taskId: `33333333-3333-4333-8333-0000000000${String(n).padStart(2, '0')}`,
  clientId: user, clientName: 'Synthetic client', engagementId: engagement, engagementServiceRoute: 'Audit',
  engagementPeriod: '2026', title: `Queue task ${n}`, phase: 'AUDIT_FIELDWORK', ordinal: n, taskStatus,
  assigneeUserId: null, assigneeDisplayName: null, dueDate, mappingVersionId: null, fsliCode: null,
  unmetTaskDependencies: 0, unmetEvidencePrerequisites: 0, canComplete, canChangeDueDate: false, revision: 1,
  pbcRequestId: null, pbcState: null, reviewState: null, reviewComment: null,
});
const queue = {
  view: 'MY_TASKS', hasMore: false, nextCursor: null,
  tasks: [task(1, 'OPEN', '2026-10-11', true), task(2, 'IN_PROGRESS', '2026-10-15'), task(3, 'COMPLETED', null)],
};

describe('work queue display modes', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [WorkflowWorkQueue],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: user, firmId: user, generation: '1', staff: true });
  });
  afterEach(() => http.verify());

  function setup() {
    const fixture = TestBed.createComponent(WorkflowWorkQueue);
    fixture.detectChanges();
    http.expectOne((r) => r.url.startsWith('/api/ui/work/tasks?')).flush(queue);
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
    // The only action a card offers is the task's own completion command, and only where the server allows it.
    const actions = [...root.querySelectorAll('.task-card button')].map((b) => b.textContent?.trim());
    expect(actions.filter(x => x !== 'Review prerequisites' && x !== 'Review task ordering')).toEqual(['Complete task']);
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
});
