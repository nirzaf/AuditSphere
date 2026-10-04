import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { SessionService } from '../../core/session';
import { decodeResources, ResourcePlanning, resourceWeekStarts } from './resources';
import {
  PlanningPreview,
  PlanningReceipt,
  decodePlanningFields,
  decodePlanningReceipt,
} from './planning-command-contracts';
import { ResourceEditor, resourceMinutes, validResourceDate } from './resource-editor';
const id = '11111111-1111-4111-8111-111111111111',
  engagement = '22222222-2222-4222-8222-222222222222';
const workspace = {
  grid: {
    firstWeek: '2026-10-05',
    weeks: 1,
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
    sessionStorage.clear();
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
  function setup(response = workspace) {
    const fixture = TestBed.createComponent(ResourcePlanning);
    fixture.detectChanges();
    http.expectOne((r) => r.url.startsWith('/api/ui/practice/resources?')).flush(response);
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
    expect(text).toContain('approved actual 10 h');
    expect(text).toContain('actual 50%');
    expect(text).toContain('Synthetic engagement');
    expect(text).toContain('30 hours');
    expect(text).toContain('does not assign an AuditSphere role');
    expect(
      f.nativeElement
        .querySelector('td[aria-label*="Synthetic planner week of"]')
        ?.getAttribute('aria-label'),
    ).toContain('approved actual 10 hours');
    f.destroy();
  });
  it('keeps selected week columns and an aligned empty row when there are no active staff', () => {
    expect(resourceWeekStarts('2026-10-05', 3)).toEqual([
      '2026-10-05',
      '2026-10-12',
      '2026-10-19',
    ]);
    const f = setup({
      grid: { firstWeek: '2026-10-05', weeks: 3, rows: [] },
      engagements: [],
    });
    const table = f.nativeElement.querySelector('table[aria-label="Resource grid"]') as HTMLTableElement;
    const headers = [...table.querySelectorAll('thead th')].map((h) => h.textContent?.trim());
    expect(headers.slice(-3)).toEqual([
      'Week of 2026-10-05',
      'Week of 2026-10-12',
      'Week of 2026-10-19',
    ]);
    expect(table.querySelector('tbody td')?.getAttribute('colspan')).toBe('6');
    f.destroy();
  });
  it('rejects empty, oversized or row-misaligned resource ranges', () => {
    expect(() => resourceWeekStarts('2026-10-05', 0)).toThrow();
    expect(() => resourceWeekStarts('2026-10-05', 13)).toThrow();
    expect(() => decodeResources({ ...workspace, grid: { ...workspace.grid, weeks: 2 } })).toThrow();
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
  function fakePreview(body: any): PlanningPreview {
    return {
      requestId: body.requestId,
      requestHash: 'a'.repeat(64),
      reviewBasis: 'b'.repeat(64),
      fields: body.fields,
      before: {
        userName: 'Synthetic planner',
        department: 'Tax',
        skills: 'IFRS',
        weeklyCapacityMinutes: 1200,
        targetUtilizationPercent: '75',
        plannedMinutes: 1800,
        engagementLabel: 'Synthetic engagement',
        weekStart: body.fields.weekStart,
        capacityMinutes: 1200,
        unavailableMinutes: 0,
        totalPlannedMinutes: 1800,
      },
      effect: 'Updates local planning only.',
    };
  }
  function fakeReceipt(p: PlanningPreview): PlanningReceipt {
    return {
      id: engagement,
      actorId: id,
      targetUserId: id,
      requestId: p.requestId,
      requestHash: p.requestHash,
      reviewBasis: p.reviewBasis,
      kind: p.fields.kind,
      resourceId: engagement,
      preview: p,
      createdAt: '2026-10-03T14:00:00Z',
    };
  }
  async function review(f: ReturnType<typeof setup>, kind: string): Promise<PlanningPreview> {
    await submit(f, kind);
    const request = http.expectOne('/api/ui/practice/resources/preview'),
      p = fakePreview(request.request.body);
    request.flush({ value: p });
    await new Promise((resolve) => setTimeout(resolve, 0));
    TestBed.tick();
    return p;
  }
  it('requires exact assent and preserves independent unsaved forms after a retained receipt', async () => {
    const f = setup(),
      a = editor(f, 'allocation'),
      c = editor(f, 'certification');
    c.model.update((m) => ({ ...m, user: id, name: 'Unsubmitted certificate' }));
    a.model.update((m) => ({ ...m, user: id, engagement, hours: '30', week: '2026-10-05' }));
    TestBed.tick();
    const p = await review(f, 'allocation');
    await f.componentInstance.confirmReviewed();
    http.expectNone('/api/ui/practice/resources/commands');
    expect(p.fields.plannedMinutes).toBe(1800);
    f.componentInstance.assentModel.set({ reviewed: true });
    const confirming = f.componentInstance.confirmReviewed();
    const req = http.expectOne('/api/ui/practice/resources/commands');
    expect(req.request.body.fields).toEqual(p.fields);
    expect(req.request.body.reviewed).toBe(true);
    const stored = JSON.stringify(sessionStorage);
    expect(stored).not.toContain('Unsubmitted certificate');
    expect(stored).not.toContain('plannedMinutes');
    req.flush({ value: fakeReceipt(p) });
    await confirming;
    TestBed.tick();
    f.componentInstance.acknowledge();
    TestBed.tick();
    expect(c.model().name).toBe('Unsubmitted certificate');
    http.expectNone((r) => r.method === 'GET');
    f.destroy();
  });
  it('recovers a lost additive response after reload without another POST or restored assent', async () => {
    let f = setup();
    const c = editor(f, 'certification');
    c.model.update((m) => ({ ...m, user: id, name: 'Test qualification' }));
    TestBed.tick();
    const p = await review(f, 'certification');
    f.componentInstance.assentModel.set({ reviewed: true });
    const confirming = f.componentInstance.confirmReviewed();
    http
      .expectOne('/api/ui/practice/resources/commands')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await confirming;
    TestBed.tick();
    expect(f.componentInstance.cmd.uncertain()).toBe(true);
    await f.componentInstance.send('certification', c.model());
    http.expectNone((r) => r.method === 'POST');
    f.destroy();
    f = setup();
    expect(f.componentInstance.pending()?.requestId).toBe(p.requestId);
    expect(f.componentInstance.assentModel().reviewed).toBe(false);
    const verifying = f.componentInstance.verifyReceipt();
    http
      .expectOne(
        '/api/ui/practice/resources/receipts/' + p.requestId + '?requestHash=' + p.requestHash,
      )
      .flush({ found: true, receipt: fakeReceipt(p) });
    await verifying;
    TestBed.tick();
    expect(f.nativeElement.textContent).toContain('Test qualification');
    f.componentInstance.acknowledge();
    http.expectOne((r) => r.url.startsWith('/api/ui/practice/resources?')).flush(workspace);
    TestBed.tick();
    http.expectNone((r) => r.method === 'POST');
    f.destroy();
  });
  it('clears protected fields and ignores late preview after session loss', async () => {
    const f = setup(),
      a = editor(f, 'allocation');
    a.model.update((m) => ({ ...m, user: id, engagement, week: '2026-10-05' }));
    TestBed.tick();
    await submit(f, 'allocation');
    const req = http.expectOne('/api/ui/practice/resources/preview'),
      p = fakePreview(req.request.body);
    TestBed.inject(SessionService).clear();
    TestBed.tick();
    expect(f.nativeElement.textContent).not.toContain('Synthetic planner');
    req.flush({ value: p });
    await new Promise((resolve) => setTimeout(resolve, 0));
    TestBed.tick();
    expect(f.componentInstance.preview()).toBeNull();
    expect(f.componentInstance.cmd.message()).toBe('');
    f.destroy();
  });
  it('storage refusal prevents mutation dispatch', async () => {
    const f = setup(),
      c = editor(f, 'certification');
    c.model.update((m) => ({ ...m, user: id, name: 'Test qualification' }));
    TestBed.tick();
    await review(f, 'certification');
    f.componentInstance.assentModel.set({ reviewed: true });
    const denied = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('Synthetic quota');
    });
    await f.componentInstance.confirmReviewed();
    http.expectNone('/api/ui/practice/resources/commands');
    expect(f.componentInstance.cmd.message()).toContain('No mutation was submitted');
    denied.mockRestore();
    f.destroy();
  });
  it('edits invalidate the exact preview and require fresh assent', async () => {
    const f = setup(),
      c = editor(f, 'certification');
    c.model.update((m) => ({ ...m, user: id, name: 'Original qualification' }));
    TestBed.tick();
    await review(f, 'certification');
    f.componentInstance.assentModel.set({ reviewed: true });
    f.componentInstance.cancelReview();
    c.model.update((m) => ({ ...m, name: 'Changed qualification' }));
    TestBed.tick();
    const p = await review(f, 'certification');
    expect(p.fields.name).toBe('Changed qualification');
    expect(f.componentInstance.assentModel().reviewed).toBe(false);
    await f.componentInstance.confirmReviewed();
    http.expectNone('/api/ui/practice/resources/commands');
    f.destroy();
  });
  it('an absent receipt requires explicit closure and an authorized refresh', async () => {
    const f = setup(),
      c = editor(f, 'certification');
    c.model.update((m) => ({ ...m, user: id, name: 'Qualification' }));
    TestBed.tick();
    const p = await review(f, 'certification');
    f.componentInstance.assentModel.set({ reviewed: true });
    const confirming = f.componentInstance.confirmReviewed();
    http
      .expectOne('/api/ui/practice/resources/commands')
      .flush({}, { status: 409, statusText: 'Changed' });
    await confirming;
    const verifying = f.componentInstance.verifyReceipt();
    http
      .expectOne(
        '/api/ui/practice/resources/receipts/' + p.requestId + '?requestHash=' + p.requestHash,
      )
      .flush({ found: false, receipt: null });
    await verifying;
    expect(f.componentInstance.cmd.uncertain()).toBe(true);
    f.componentInstance.acknowledge();
    http.expectOne((r) => r.url.startsWith('/api/ui/practice/resources?')).flush(workspace);
    TestBed.tick();
    expect(f.componentInstance.pending()).toBeNull();
    f.destroy();
  });
  it('mixed planning fields and mismatched receipt metadata fail closed', () => {
    const f = setup(),
      c = editor(f, 'certification');
    const body = {
      requestId: id,
      fields: {
        kind: 'CERTIFICATION',
        userId: id,
        engagementId: null,
        department: null,
        skills: null,
        weeklyCapacityMinutes: null,
        targetUtilizationPercent: null,
        name: 'Qualification',
        expiresOn: null,
        startDate: null,
        endDate: null,
        availabilityKind: null,
        minutesPerDay: null,
        weekStart: null,
        plannedMinutes: null,
      },
    };
    const p = fakePreview(body);
    expect(() => decodePlanningFields({ ...p.fields, plannedMinutes: 100 })).toThrow();
    expect(() => decodePlanningReceipt({ ...fakeReceipt(p), targetUserId: engagement })).toThrow();
    f.destroy();
  });
});
