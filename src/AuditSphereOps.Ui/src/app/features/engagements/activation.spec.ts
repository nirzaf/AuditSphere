import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { EngagementActivationReview } from './activation';
import {
  decodeActivationLookup,
  decodeActivationReceipt,
  decodeActivationState,
} from './activation-contracts';

const id = '11111111-1111-4111-8111-111111111111',
  other = '22222222-2222-4222-8222-222222222222';
const basis = 'a'.repeat(64),
  hash = 'b'.repeat(64),
  url = '/api/ui/engagements/' + id + '/activation-review';
const state = {
  engagementId: id,
  clientId: other,
  clientName: 'Synthetic client',
  serviceRoute: 'FinancialStatementAudit',
  serviceProfileId: 'Synthetic annual audit',
  periodStart: '2026-01-01',
  periodEnd: '2026-12-31',
  status: 'Draft',
  engagementGeneration: '9007199254740993',
  clientGeneration: '9007199254740993',
  decision: {
    id: other,
    decision: 'Accepted',
    path: 'NEW_CLIENT',
    generation: '9007199254740993',
    decidedAt: '2026-10-03T12:30:00Z',
  },
  activeHolds: 0,
  eligible: true,
  reviewBasis: basis,
  blockers: [],
};
function receipt(requestId: string) {
  return {
    id: other,
    engagementId: id,
    clientId: other,
    actorId: id,
    requestId,
    requestHash: hash,
    reviewBasis: basis,
    acceptanceDecisionId: other,
    acceptancePath: 'NEW_CLIENT',
    clientGeneration: '9007199254740993',
    engagementGeneration: '9007199254740993',
    resultGeneration: '9007199254740994',
    activatedAt: '2026-10-03T12:30:00Z',
  };
}

describe('Activation projection contracts', () => {
  it('preserves exact revisions and accepts only consistent eligibility and receipt identities', () => {
    expect(
      decodeActivationState({ ...state, privateDetails: 'PRIVATE' }).engagementGeneration,
    ).toBe('9007199254740993');
    expect(decodeActivationState({ ...state, privateDetails: 'PRIVATE' })).not.toHaveProperty(
      'privateDetails',
    );
    for (const value of [
      { ...state, engagementGeneration: 9007199254740993 },
      { ...state, engagementGeneration: '9223372036854775808' },
      { ...state, activeHolds: 1 },
      { ...state, status: 'Active' },
      { ...state, decision: { ...state.decision, decision: 'AcceptedWithConditions' } },
      { ...state, decision: { ...state.decision, generation: '1' } },
      { ...state, blockers: [{ code: 'held', message: 'Held' }] },
      { ...state, eligible: false },
    ])
      expect(() => decodeActivationState(value)).toThrow();
    expect(() =>
      decodeActivationReceipt({ ...receipt(id), resultGeneration: '9007199254740993' }),
    ).toThrow();
    expect(() => decodeActivationReceipt({ ...receipt(id), activatedAt: 'bad' })).toThrow();
    expect(() => decodeActivationLookup({ found: true, receipt: null })).toThrow();
  });
});

describe('Native Partner activation review and request recovery', () => {
  let ids: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    sessionStorage.clear();
    ids = new BehaviorSubject(convertToParamMap({ id }));
    TestBed.configureTestingModule({
      imports: [EngagementActivationReview],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: ids,
            snapshot: { queryParamMap: convertToParamMap({ holdPage: '2', holdPageSize: '25' }) },
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
    sessionStorage.clear();
  });
  function open() {
    const f = TestBed.createComponent(EngagementActivationReview);
    f.detectChanges();
    TestBed.tick();
    return f;
  }
  function read(value: object = state) {
    const active = TestBed.inject(HttpTestingController)
      .match((r) => r.method === 'GET' && !r.url.includes('/receipts/'))
      .filter((r) => !r.cancelled);
    expect(active).toHaveLength(1);
    active[0].flush(value);
    TestBed.tick();
  }
  async function preview(c: EngagementActivationReview, target = url, value = state) {
    const pending = c.prepare();
    const r = TestBed.inject(HttpTestingController).expectOne(target + '/preview');
    const body = r.request.body;
    r.flush({
      value: {
        engagementId: value.engagementId,
        requestId: body.requestId,
        reviewBasis: value.reviewBasis,
        requestHash: hash,
      },
    });
    await pending;
    TestBed.tick();
    return body;
  }
  async function unknown(c: EngagementActivationReview) {
    c.model.set({ reviewed: true });
    TestBed.tick();
    const dispatch = c.execute();
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({}, { status: 503, statusText: 'Unknown' });
    await dispatch;
    TestBed.tick();
  }

  it('renders exact acceptance, restores return location, and requires fresh explicit assent', async () => {
    const f = open();
    read();
    const c = f.componentInstance;
    expect(c.returnParams).toEqual({ holdPage: 2, holdPageSize: 25 });
    expect(f.nativeElement.textContent).toContain('9007199254740993');
    expect(f.nativeElement.textContent).toContain('2026-10-03 12:30 UTC');
    await preview(c);
    await c.execute();
    TestBed.inject(HttpTestingController).expectNone(url);
    expect(f.nativeElement.querySelector('button[type="submit"]').disabled).toBe(true);
    c.model.set({ reviewed: true });
    TestBed.tick();
    c.refresh();
    TestBed.tick();
    expect(c.model().reviewed).toBe(false);
    expect(c.preview()).toBeNull();
    read({ ...state, reviewBasis: 'c'.repeat(64) });
    await c.execute();
    TestBed.inject(HttpTestingController).expectNone(url);
  });
  it('blocks unknown resubmission and reconciles only its actor-owned immutable receipt', async () => {
    const f = open();
    read();
    const c = f.componentInstance,
      r = await preview(c);
    await unknown(c);
    expect(c.uncertain()).toBe(true);
    expect(c.confirmNavigation()).toBe(false);
    await c.prepare();
    await c.execute();
    TestBed.inject(HttpTestingController).expectNone(url + '/preview');
    TestBed.inject(HttpTestingController).expectNone(url);
    const check = c.reconcile();
    TestBed.inject(HttpTestingController)
      .expectOne(url + '/receipts/' + r.requestId + '?requestHash=' + hash)
      .flush({ found: true, receipt: receipt(r.requestId) });
    await check;
    TestBed.tick();
    expect(c.receipt()?.resultGeneration).toBe('9007199254740994');
    expect(c.uncertain()).toBe(true);
    c.acknowledge();
    read({
      ...state,
      status: 'Active',
      eligible: false,
      reviewBasis: 'c'.repeat(64),
      blockers: [{ code: 'engagement.not-draft', message: 'Already active' }],
    });
    expect(f.nativeElement.textContent).toContain('Engagement already active');
    expect(f.nativeElement.textContent).not.toContain('Activation blocked');
    expect(c.pending()).toBeNull();
    expect(c.uncertain()).toBe(false);
    expect(c.model().reviewed).toBe(false);
    expect(sessionStorage.length).toBe(0);
  });
  it('retains only request identity across reload and requires fresh review before an absent-result retry', async () => {
    const first = open();
    read();
    const r = await preview(first.componentInstance);
    await unknown(first.componentInstance);
    first.destroy();
    const values = Array.from({ length: sessionStorage.length }, (_, i) =>
      sessionStorage.getItem(sessionStorage.key(i)!),
    ).join();
    expect(values).not.toContain('reviewed');
    expect(values).not.toContain('Synthetic annual audit');
    const next = open();
    const fresh = { ...state, reviewBasis: 'c'.repeat(64) };
    read(fresh);
    const c = next.componentInstance;
    expect(c.pending()?.requestId).toBe(r.requestId);
    expect(c.uncertain()).toBe(true);
    expect(c.model().reviewed).toBe(false);
    const check = c.reconcile();
    TestBed.inject(HttpTestingController)
      .expectOne(url + '/receipts/' + r.requestId + '?requestHash=' + hash)
      .flush({ found: false, receipt: null });
    await check;
    TestBed.tick();
    c.reviewAgain();
    read(fresh);
    expect(c.uncertain()).toBe(false);
    expect(c.model().reviewed).toBe(false);
    const retried = await preview(c, url, fresh);
    expect(retried.requestId).toBe(r.requestId);
    expect(retried.reviewBasis).toBe(fresh.reviewBasis);
    await c.execute();
    TestBed.inject(HttpTestingController).expectNone(url);
    c.model.set({ reviewed: true });
    TestBed.tick();
    const dispatch = c.execute();
    const post = TestBed.inject(HttpTestingController).expectOne(url);
    expect(post.request.body.expectedRequestHash).toBe(hash);
    post.flush({ value: { ...receipt(r.requestId), reviewBasis: fresh.reviewBasis } });
    await dispatch;
    TestBed.tick();
    read({
      ...fresh,
      status: 'Active',
      eligible: false,
      blockers: [{ code: 'engagement.not-draft', message: 'Already active' }],
    });
    expect(c.pending()).toBeNull();
  });
  it('treats malformed acknowledgements and another actor receipt as unknown, preserving the reference', async () => {
    const f = open();
    read();
    const c = f.componentInstance,
      r = await preview(c);
    c.model.set({ reviewed: true });
    TestBed.tick();
    const dispatch = c.execute();
    TestBed.inject(HttpTestingController).expectOne(url).flush({ value: { id } });
    await dispatch;
    TestBed.tick();
    expect(c.uncertain()).toBe(true);
    expect(c.receipt()).toBeNull();
    const check = c.reconcile();
    TestBed.inject(HttpTestingController)
      .expectOne(url + '/receipts/' + r.requestId + '?requestHash=' + hash)
      .flush({ found: true, receipt: { ...receipt(r.requestId), actorId: other } });
    await check;
    TestBed.tick();
    expect(c.receipt()).toBeNull();
    expect(c.pending()?.requestId).toBe(r.requestId);
    expect(c.uncertain()).toBe(true);
  });
  it('fences a response from A to B to A without clearing a newer busy request', async () => {
    const f = open();
    read();
    const c = f.componentInstance,
      r = await preview(c);
    c.model.set({ reviewed: true });
    TestBed.tick();
    const oldDispatch = c.execute();
    const old = TestBed.inject(HttpTestingController).expectOne(url);
    ids.next(convertToParamMap({ id: other }));
    TestBed.tick();
    read({ ...state, engagementId: other });
    ids.next(convertToParamMap({ id }));
    TestBed.tick();
    read();
    expect(c.uncertain()).toBe(true);
    const current = c.reconcile(),
      get = TestBed.inject(HttpTestingController).expectOne(
        url + '/receipts/' + r.requestId + '?requestHash=' + hash,
      );
    old.flush({ value: receipt(r.requestId) });
    await oldDispatch;
    TestBed.tick();
    expect(c.busy()).toBe(true);
    expect(c.receipt()).toBeNull();
    get.flush({ found: true, receipt: receipt(r.requestId) });
    await current;
    TestBed.tick();
    expect(c.busy()).toBe(false);
    expect(c.receipt()?.requestId).toBe(r.requestId);
  });
  it('removes protected context and ignores callbacks after destruction or session loss', async () => {
    const f = open();
    read();
    const c = f.componentInstance,
      r = await preview(c);
    c.model.set({ reviewed: true });
    TestBed.tick();
    const dispatch = c.execute();
    const post = TestBed.inject(HttpTestingController).expectOne(url);
    TestBed.inject(SessionService).clear();
    TestBed.tick();
    expect(c.state.data()).toBeNull();
    expect(c.pending()).toBeNull();
    expect(c.model().reviewed).toBe(false);
    expect(sessionStorage.length).toBe(0);
    f.destroy();
    post.flush({ value: receipt(r.requestId) });
    await dispatch;
    TestBed.tick();
    expect(c.receipt()).toBeNull();
    expect(c.message()).toBe('');
  });
  it('refuses dispatch if recovery storage fails and clears content on a scope refusal', async () => {
    const f = open();
    read();
    const c = f.componentInstance;
    await preview(c);
    c.model.set({ reviewed: true });
    TestBed.tick();
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('Unavailable');
    });
    await c.execute();
    TestBed.inject(HttpTestingController).expectNone(url);
    expect(c.message()).toContain('No activation was sent.');
    vi.restoreAllMocks();
    c.refresh();
    TestBed.tick();
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({}, { status: 403, statusText: 'Forbidden' });
    TestBed.tick();
    expect(c.state.data()).toBeNull();
    expect(c.preview()).toBeNull();
    expect(f.nativeElement.textContent).not.toContain('Synthetic annual audit');
  });
});
