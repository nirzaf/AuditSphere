import { TestBed, DeferBlockBehavior, DeferBlockState } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { EngagementDetail } from './engagement';
import { decodeEngagement, engagementLocation } from './engagement-contracts';
const id = '11111111-1111-4111-8111-111111111111',
  other = '22222222-2222-4222-8222-222222222222';
const payload = {
  id,
  clientId: id,
  clientName: 'Synthetic client',
  serviceRoute: 'Audit',
  status: 'Draft',
  periodStart: '2026-01-01',
  periodEnd: '2026-12-31',
  generation: '9007199254740993',
  serviceProfileId: 'Synthetic annual profile',
  createdAt: '2026-01-02T12:30:00Z',
  professionalWorkBlocked: true,
  canActivate: true,
  canViewClientProfile: false,
  holdMetrics: { total: 27, active: 13, released: 14 },
  paging: { holdPage: 0, holdPageSize: 10 },
  holds: [
    {
      id,
      kind: 'Acceptance',
      reason: 'Synthetic review',
      released: false,
      createdAt: '2026-01-02T12:30:00Z',
      releasedAt: null,
    },
  ],
};
const planning = {
  team: [],
  latestBudgetVersion: '0',
  draft: null,
  canManageStaffing: true,
  canPrepareBudget: true,
  candidates: [],
  budget: null,
  budgetState: 'UNAVAILABLE',
};
describe('Engagement projection bounds', () => {
  it('keeps exact revision and complete counts while excluding extra fields', () => {
    expect(decodeEngagement({ ...payload, privateProfile: 'PRIVATE' }).generation).toBe(
      '9007199254740993',
    );
    expect(decodeEngagement({ ...payload, privateProfile: 'PRIVATE' })).not.toHaveProperty(
      'privateProfile',
    );
    for (const v of [
      { ...payload, generation: 9007199254740993 },
      { ...payload, generation: '9223372036854775808' },
      { ...payload, holdMetrics: { total: 27, active: 13, released: 13 } },
      { ...payload, holds: [payload.holds[0], payload.holds[0]] },
      { ...payload, paging: { holdPage: 0, holdPageSize: 100 } },
      { ...payload, createdAt: 'bad' },
    ])
      expect(() => decodeEngagement(v)).toThrow();
  });
  it('accepts only bounded canonical hold page parameters', () => {
    expect(
      engagementLocation(
        (k) => (({ holdPage: '2', holdPageSize: '25' }) as Record<string, string>)[k],
      ),
    ).toEqual({ holdPage: 2, holdPageSize: 25 });
    for (const q of [
      { holdPage: '01' },
      { holdPage: '-1' },
      { holdPage: '10001' },
      { holdPageSize: '100' },
    ])
      expect(
        engagementLocation((k) => (q as Record<string, string | undefined>)[k] ?? null),
      ).toBeNull();
  });
});
describe('Engagement read and command lifetime', () => {
  let ids: BehaviorSubject<ReturnType<typeof convertToParamMap>>,
    queries: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    ids = new BehaviorSubject(convertToParamMap({ id }));
    queries = new BehaviorSubject(convertToParamMap({}));
    TestBed.configureTestingModule({
      imports: [EngagementDetail],
      deferBlockBehavior: DeferBlockBehavior.Manual,
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: ids,
            queryParamMap: queries,
            snapshot: { queryParamMap: queries.value },
          },
        },
      ],
    });
    TestBed.inject(SessionService).current.set({
      userId: id,
      firmId: id,
      generation: '1',
      staff: true,
    });
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });
  function open() {
    const f = TestBed.createComponent(EngagementDetail);
    f.detectChanges();
    TestBed.tick();
    return f;
  }
  function read() {
    const active = TestBed.inject(HttpTestingController)
      .match((r) => r.method === 'GET' && !r.url.endsWith('/planning'))
      .filter((r) => !r.cancelled);
    expect(active).toHaveLength(1);
    return active[0];
  }
  it('allows same-engagement paging but delegates a different route to planning', async () => {
    const f = open();
    read().flush(payload);
    TestBed.tick();
    const confirmation = vi.fn().mockResolvedValue(false);
    vi.spyOn(f.componentInstance, 'planning').mockReturnValue({
      confirmNavigation: confirmation,
    } as never);
    expect(
      await f.componentInstance.confirmNavigation('/app/engagements/' + id + '?holdPage=1'),
    ).toBe(true);
    expect(
      await f.componentInstance.confirmNavigation('/ui/app/engagements/' + id + '?holdPage=1'),
    ).toBe(true);
    expect(confirmation).not.toHaveBeenCalled();
    expect(await f.componentInstance.confirmNavigation('/app/engagements/' + other)).toBe(false);
    expect(await f.componentInstance.confirmNavigation('/app/engagements/' + id + '/pbc')).toBe(
      false,
    );
    expect(confirmation).toHaveBeenCalledTimes(2);
  });
  it('renders source metadata and complete summary with scoped navigation and paging', () => {
    const f = open();
    read().flush(payload);
    TestBed.tick();
    expect(f.nativeElement.textContent).toContain('Synthetic annual profile');
    expect(f.nativeElement.textContent).toContain('2026-01-02 12:30 UTC');
    expect(f.nativeElement.textContent).toContain('27 recorded holds');
    expect(
      f.componentInstance.workBlocked(
        decodeEngagement({ ...payload, professionalWorkBlocked: false }),
      ),
    ).toBe(true);
    expect(f.nativeElement.querySelector('a[href="/app/clients/' + id + '"]')).toBeNull();
    const nav = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    f.componentInstance.page(1);
    expect(nav).toHaveBeenCalledWith(
      [],
      expect.objectContaining({ queryParams: { holdPage: 1, holdPageSize: 10 } }),
    );
  });
  it('cancels superseded reads and rejects another identity or page', () => {
    const f = open(),
      old = read();
    ids.next(convertToParamMap({ id: other }));
    TestBed.tick();
    expect(old.cancelled).toBe(true);
    read().flush(payload);
    TestBed.tick();
    expect(f.componentInstance.data()).toBeNull();
    queries.next(convertToParamMap({ holdPage: '2' }));
    TestBed.tick();
    read().flush({ ...payload, id: other });
    TestBed.tick();
    expect(f.componentInstance.data()).toBeNull();
  });
  it('preserves the planning editor during hold paging and removes it on refusal', async () => {
    const f = open();
    read().flush(payload);
    TestBed.tick();
    const blocks = await f.getDeferBlocks();
    await blocks[0].render(DeferBlockState.Complete);
    TestBed.tick();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/ui/engagements/' + id + '/planning')
      .flush(planning);
    TestBed.tick();
    const child = f.componentInstance.planning()!;
    child.currency = 'USD';
    child.budgetLines[0].forecastMinutes = 123;
    queries.next(convertToParamMap({ holdPage: '1' }));
    TestBed.tick();
    expect(f.componentInstance.planning()).toBe(child);
    read().flush({ ...payload, paging: { holdPage: 1, holdPageSize: 10 } });
    TestBed.tick();
    expect(f.componentInstance.planning()).toBe(child);
    expect(child.currency).toBe('USD');
    expect(child.budgetLines[0].forecastMinutes).toBe(123);
    f.componentInstance.load();
    read().flush({}, { status: 403, statusText: 'Forbidden' });
    TestBed.tick();
    expect(f.componentInstance.data()).toBeNull();
    expect(f.componentInstance.planning()).toBeUndefined();
    expect(f.nativeElement.textContent).not.toContain('Synthetic annual profile');
  });
  it('clears protected read content on session loss', () => {
    const f = open();
    read().flush(payload);
    TestBed.tick();
    TestBed.inject(SessionService).clear();
    TestBed.tick();
    expect(f.componentInstance.data()).toBeNull();
    expect(f.nativeElement.textContent).not.toContain('Synthetic annual profile');
    f.destroy();
  });
});
