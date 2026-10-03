import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { SessionService } from '../../core/session';
import { ResourcePlanning } from './resources';
import { ResourceEditor, resourceMinutes, validResourceDate } from './resource-editor';
const id = '11111111-1111-4111-8111-111111111111',
  engagement = '22222222-2222-4222-8222-222222222222';
const workspace = {
  grid: {
    firstWeek: '2026-10-05',
    weeks: 4,
    rows: [
      {
        userId: id,
        name: 'Synthetic planner',
        department: 'Tax',
        skills: ['IFRS'],
        certifications: [],
        weeklyCapacityMinutes: 1200,
        targetUtilizationPercent: '75.000000',
        hasProfile: true,
        weeks: [
          {
            weekStart: '2026-10-05',
            capacityMinutes: 1200,
            unavailableMinutes: 0,
            plannedMinutes: 1800,
            approvedActualMinutes: 600,
            plannedUtilizationPercent: '150',
            actualUtilizationPercent: '50',
            overAllocated: true,
          },
        ],
        allocations: [
          {
            engagementId: engagement,
            engagementLabel: 'Synthetic engagement',
            weekStart: '2026-10-05',
            plannedMinutes: 1800,
          },
        ],
      },
    ],
  },
  engagements: [{ id: engagement, label: 'Synthetic engagement' }],
};
describe('Resource planning forms', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ResourcePlanning],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({
      userId: id,
      firmId: id,
      generation: '1',
      staff: true,
    });
  });
  afterEach(() => http.verify());
  function setup() {
    const fixture = TestBed.createComponent(ResourcePlanning);
    fixture.detectChanges();
    http.expectOne((r) => r.url.startsWith('/api/ui/practice/resources?')).flush(workspace);
    TestBed.tick();
    return fixture;
  }
  function editor(f: ReturnType<typeof setup>, kind: string): ResourceEditor {
    return f.debugElement
      .queryAll(By.directive(ResourceEditor))
      .map((e) => e.componentInstance as ResourceEditor)
      .find((e) => e.kind() === kind)!;
  }
  async function submit(f: ReturnType<typeof setup>, kind: string) {
    const form = f.nativeElement.querySelector(
      'form[aria-label="' +
        (
          {
            profile: 'Save profile',
            certification: 'Add certification',
            availability: 'Record unavailability',
            allocation: 'Save allocation',
          } as Record<string, string>
        )[kind] +
        '"]',
    ) as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    TestBed.tick();
    await Promise.resolve();
  }
  it('validates bounded hours and real calendar dates', () => {
    expect(resourceMinutes('80')).toBe(4800);
    expect(resourceMinutes('80.01')).toBeNull();
    expect(resourceMinutes('1.25')).toBe(75);
    expect(resourceMinutes('-1')).toBeNull();
    expect(validResourceDate('2026-02-29')).toBe(false);
    expect(validResourceDate('2028-02-29')).toBe(true);
    expect(validResourceDate('0000-01-01')).toBe(false);
  });
  it('shows allocation, capacity, approved actual and over-allocation distinctly', () => {
    const f = setup();
    const text = f.nativeElement.textContent;
    expect(text).toContain('Over-allocated');
    expect(text).toContain('actual 50%');
    expect(text).toContain('Synthetic engagement');
    expect(text).toContain('30 hours');
    expect(text).toContain('does not assign an AuditSphere role');
    f.destroy();
  });
  it('populates selected profile from persisted state and rejects invalid percentage before POST', async () => {
    const f = setup(),
      e = editor(f, 'profile');
    e.model.update((m) => ({ ...m, user: id }));
    TestBed.tick();
    expect(e.model().department).toBe('Tax');
    expect(e.model().capacity).toBe('20');
    expect(e.model().target).toBe('75');
    expect(e.fields().valid()).toBe(true);
    e.model.update((m) => ({ ...m, target: '101' }));
    TestBed.tick();
    await submit(f, 'profile');
    http.expectNone('/api/ui/practice/resources/profiles');
    expect(f.nativeElement.textContent).toContain('Enter a percentage from 0 to 100');
    f.destroy();
  });
  it('requires certification identity and rejects reversed unavailability dates', async () => {
    const f = setup(),
      c = editor(f, 'certification'),
      a = editor(f, 'availability');
    c.model.update((m) => ({ ...m, user: id, name: '   ' }));
    a.model.update((m) => ({ ...m, user: id, from: '2026-10-10', to: '2026-10-01' }));
    TestBed.tick();
    await submit(f, 'certification');
    await submit(f, 'availability');
    http.expectNone((r) => r.method === 'POST');
    expect(f.nativeElement.textContent).toContain('end date cannot precede');
    f.destroy();
  });
  it('sends an exact allocation once and resets only its own form', async () => {
    const f = setup(),
      a = editor(f, 'allocation'),
      c = editor(f, 'certification');
    c.model.update((m) => ({ ...m, user: id, name: 'Unsubmitted certificate' }));
    a.model.update((m) => ({ ...m, user: id, engagement, hours: '30', week: '2026-10-05' }));
    TestBed.tick();
    await submit(f, 'allocation');
    const req = http.expectOne('/api/ui/practice/resources/allocations');
    expect(req.request.body).toEqual({
      engagementId: engagement,
      userId: id,
      weekStart: '2026-10-05',
      plannedMinutes: 1800,
    });
    req.flush({ succeeded: true });
    await Promise.resolve();
    await Promise.resolve();
    TestBed.tick();
    expect(c.model().name).toBe('Unsubmitted certificate');
    http.expectNone((r) => r.method === 'GET');
    f.destroy();
  });
  it('unknown additive outcome locks further commands and is never silently retried', async () => {
    const f = setup(),
      c = editor(f, 'certification');
    c.model.update((m) => ({ ...m, user: id, name: 'Test qualification' }));
    TestBed.tick();
    await submit(f, 'certification');
    http
      .expectOne('/api/ui/practice/resources/certifications')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await Promise.resolve();
    await Promise.resolve();
    TestBed.tick();
    expect(f.componentInstance.cmd.uncertain()).toBe(true);
    expect(f.nativeElement.textContent).toContain('administrator reconciliation');
    await f.componentInstance.send('certification', c.model());
    http.expectNone((r) => r.method === 'POST');
    f.destroy();
  });
  it('clears protected fields and ignores late command results after session loss', async () => {
    const f = setup(),
      a = editor(f, 'allocation');
    a.model.update((m) => ({ ...m, user: id, engagement, week: '2026-10-05' }));
    TestBed.tick();
    await submit(f, 'allocation');
    const req = http.expectOne('/api/ui/practice/resources/allocations');
    TestBed.inject(SessionService).clear();
    TestBed.tick();
    expect(f.nativeElement.textContent).not.toContain('Synthetic planner');
    req.flush({ succeeded: true });
    await Promise.resolve();
    await Promise.resolve();
    TestBed.tick();
    expect(f.componentInstance.cmd.message()).toBe('');
    f.destroy();
  });
});
