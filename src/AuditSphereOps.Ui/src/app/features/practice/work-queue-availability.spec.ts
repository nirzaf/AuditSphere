import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { WorkspaceAvailability } from '../../core/workspace-availability';
import { WorkflowWorkQueue } from './work-queue';

const user = '11111111-1111-4111-8111-111111111111';
const firm = '22222222-2222-4222-8222-222222222222';
const tasksUrl = '/api/ui/work/tasks?view=MY_TASKS&pageSize=50&includeSummary=true';
const reviewsUrl = '/api/ui/work/reviews?pageSize=50';

describe('work availability and session fencing', () => {
  afterEach(() => TestBed.resetTestingModule());

  function start() {
    TestBed.configureTestingModule({ imports: [WorkflowWorkQueue], providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
    ] });
    const http = TestBed.inject(HttpTestingController);
    const session = TestBed.inject(SessionService);
    session.current.set({ userId: user, firmId: firm, generation: '1', staff: true });
    const fixture = TestBed.createComponent(WorkflowWorkQueue);
    fixture.detectChanges(); TestBed.tick();
    return { http, session, fixture, availability: TestBed.inject(WorkspaceAvailability) };
  }
  function availability(tasks: string, reviews: string, destinations: string[] = ['my-work']) {
    return { userId: user, firmId: firm, generation: '1', staff: true, destinations, tasks, reviews, templates: 'not_authorized', validForMs: 30000 };
  }

  it('requests only the task queue for a staff-only availability response', () => {
    const { http, fixture } = start();
    http.expectOne('/api/ui/workspace-availability').flush(availability('available', 'not_authorized'));
    TestBed.tick();
    http.expectNone(reviewsUrl);
    http.expectOne(tasksUrl).flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null, summary: null });
    TestBed.tick();
    expect(fixture.nativeElement.textContent).not.toContain('Submitted work awaiting review');
    expect(fixture.nativeElement.textContent).toContain('My tasks: 0');
    expect(fixture.nativeElement.querySelector('a[href="/app/accounting/reviews"]')).toBeNull();
    http.verify({ ignoreCancelled: true }); fixture.destroy();
  });

  it('offers the independently authorized package review queue even when workflow work is disabled', () => {
    const { http, session, fixture } = start();
    const packageLink = () => (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>('a[href="/app/accounting/reviews"]');
    expect(packageLink()).toBeNull();
    http.expectOne('/api/ui/workspace-availability').flush(availability('not_enabled', 'not_enabled', ['reviews']));
    TestBed.tick();
    expect(packageLink()?.textContent).toContain('Open financial package review queue');
    expect(fixture.nativeElement.textContent).toContain('no package total is loaded here');
    expect(fixture.nativeElement.textContent).toContain('Workflow work is not enabled');
    http.expectNone(tasksUrl); http.expectNone(reviewsUrl);
    http.expectNone(request => request.url.startsWith('/api/ui/accounting/reviews'));
    session.clear(); TestBed.tick();
    expect(packageLink()).toBeNull();
    http.expectNone(request => request.method === 'POST');
    http.verify({ ignoreCancelled: true }); fixture.destroy();
  });

  it('requests only the review inbox for a reviewer-only availability response', () => {
    const { http, fixture } = start();
    http.expectOne('/api/ui/workspace-availability').flush(availability('not_authorized', 'available'));
    TestBed.tick();
    http.expectNone(tasksUrl);
    http.expectOne(reviewsUrl).flush({ reviews: [], hasMore: false, nextCursor: null });
    TestBed.tick();
    expect(fixture.nativeElement.textContent).toContain('No additional current submissions await review');
    expect(fixture.nativeElement.textContent).not.toContain('work queue is unavailable');
    http.verify({ ignoreCancelled: true }); fixture.destroy();
  });

  it('distinguishes a disabled workflow from an authorized empty queue', () => {
    const { http, fixture } = start();
    http.expectOne('/api/ui/workspace-availability').flush(availability('not_enabled', 'not_enabled', []));
    TestBed.tick();
    http.expectNone(tasksUrl); http.expectNone(reviewsUrl);
    expect(fixture.nativeElement.textContent).toContain('Workflow work is not enabled');
    expect(fixture.nativeElement.textContent).not.toContain('My tasks: 0');
    http.verify({ ignoreCancelled: true }); fixture.destroy();
  });

  it('clears the projection and cancels delayed section reads when the session changes', () => {
    const { http, session, fixture, availability: service } = start();
    http.expectOne('/api/ui/workspace-availability').flush(availability('available', 'available'));
    TestBed.tick();
    const tasks = http.expectOne(tasksUrl); const reviews = http.expectOne(reviewsUrl);
    session.clear(); TestBed.tick();
    expect(service.current()).toBeNull();
    expect(tasks.cancelled).toBe(true); expect(reviews.cancelled).toBe(true);
    expect(() => tasks.flush({ view: 'MY_TASKS', tasks: [], hasMore: false, nextCursor: null, summary: null })).toThrow();
    expect(fixture.nativeElement.textContent).not.toContain('My tasks: 0');
    http.verify({ ignoreCancelled: true }); fixture.destroy();
  });

  it('cancels a delayed availability response when the session ends', () => {
    const { http, session, fixture, availability: service } = start();
    const request = http.expectOne('/api/ui/workspace-availability');
    session.clear(); TestBed.tick();
    expect(request.cancelled).toBe(true);
    expect(() => request.flush(availability('available', 'available'))).toThrow();
    expect(service.current()).toBeNull();
    http.expectNone(tasksUrl); http.expectNone(reviewsUrl);
    http.verify({ ignoreCancelled: true }); fixture.destroy();
  });

  it('latches a forbidden task read and restores the independently authorized review section', () => {
    const { http, fixture, availability: service } = start();
    http.expectOne('/api/ui/workspace-availability').flush(availability('available', 'available'));
    TestBed.tick();
    const tasks = http.expectOne(tasksUrl); const reviews = http.expectOne(reviewsUrl);
    tasks.flush({}, { status: 403, statusText: 'Forbidden' }); TestBed.tick();
    expect(service.current()).toBeNull(); expect(reviews.cancelled).toBe(true);
    http.expectOne('/api/ui/workspace-availability').flush(availability('available', 'available'));
    TestBed.tick();
    http.expectNone(tasksUrl);
    http.expectOne(reviewsUrl).flush({ reviews: [], hasMore: false, nextCursor: null });
    TestBed.tick();
    expect(fixture.nativeElement.textContent).toContain('The task queue is unavailable in your current scope');
    expect(fixture.nativeElement.textContent).toContain('Submitted work awaiting review');
    http.verify({ ignoreCancelled: true }); fixture.destroy();
  });

  it('clears task and review data at the returned expiry even while a refresh is pending', async () => {
    const { http, fixture, availability: service } = start();
    http.expectOne('/api/ui/workspace-availability').flush({ ...availability('available', 'available', ['my-work', 'reviews']), validForMs: 150 });
    TestBed.tick();
    const tasks = http.expectOne(tasksUrl); const reviews = http.expectOne(reviewsUrl);
    expect(fixture.nativeElement.querySelector('a[href="/app/accounting/reviews"]')).not.toBeNull();
    await new Promise(resolve => setTimeout(resolve, 220)); TestBed.tick();
    expect(service.current()).toBeNull();
    expect(tasks.cancelled).toBe(true); expect(reviews.cancelled).toBe(true);
    expect(fixture.nativeElement.querySelector('a[href="/app/accounting/reviews"]')).toBeNull();
    const refreshes = http.match('/api/ui/workspace-availability');
    expect(refreshes).toHaveLength(2); expect(refreshes[0].cancelled).toBe(true);
    refreshes[1].flush(availability('not_authorized', 'not_authorized', []));
    TestBed.tick();
    expect(fixture.nativeElement.textContent).not.toContain('My tasks: 0');
    http.verify({ ignoreCancelled: true }); fixture.destroy();
  });

  it('invalidates the shared session when the availability read receives 401', () => {
    const { http, session, fixture, availability: service } = start();
    http.expectOne('/api/ui/workspace-availability').flush({}, { status: 401, statusText: 'Unauthorized' });
    TestBed.tick();
    expect(session.current()).toBeNull(); expect(service.current()).toBeNull();
    http.expectNone(tasksUrl); http.expectNone(reviewsUrl);
    http.verify({ ignoreCancelled: true }); fixture.destroy();
  });

  it('does not accept a projection for another identity and exposes unavailable rather than zero', () => {
    const { http, fixture, availability: service } = start();
    http.expectOne('/api/ui/workspace-availability').flush({ ...availability('available', 'available'), firmId: user });
    TestBed.tick();
    expect(service.current()).toBeNull();
    http.expectNone(tasksUrl); http.expectNone(reviewsUrl);
    expect(fixture.nativeElement.textContent).toContain('Work availability is temporarily unavailable');
    expect(fixture.nativeElement.textContent).not.toContain('My tasks: 0');
    http.verify({ ignoreCancelled: true }); fixture.destroy();
  });
});
