import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { EngagementCreate } from './engagement-create';
import {
  engagementFields,
  decodeEngagementCreationLookup,
  decodeEngagementCreationState,
  validEngagement,
} from './engagement-create-contracts';

const id = '11111111-1111-4111-8111-111111111111',
  other = '22222222-2222-4222-8222-222222222222';
const basis = 'a'.repeat(64),
  hash = 'b'.repeat(64),
  url = '/api/ui/clients/' + id + '/engagement-creation';
const state = {
  clientId: id,
  clientName: 'Synthetic client',
  clientGeneration: '9007199254740993',
  reviewBasis: basis,
};
const fields = {
  serviceRoute: 'AccountingOnly',
  serviceProfile: 'SYNTHETIC',
  periodStart: '2026-01-01',
  periodEnd: '2026-12-31',
};
describe('Reviewed client engagement creation contracts', () => {
  it('allowlists bounded fields and exact generations without retaining assent', () => {
    expect(engagementFields({ ...fields, reviewed: true })).toBeNull();
    expect(engagementFields({ ...fields, serviceProfile: 'x'.repeat(101) })).toBeNull();
    expect(engagementFields({ ...fields, serviceProfile: 'Finance\n' })).toBeNull();
    expect(validEngagement({ ...fields, periodEnd: '2025-12-31' })).toBe(false);
    expect(decodeEngagementCreationState(state, '').clientGeneration).toBe('9007199254740993');
    expect(() =>
      decodeEngagementCreationState({ ...state, clientGeneration: 9007199254740993 }, ''),
    ).toThrow();
    expect(() => decodeEngagementCreationLookup({ found: true, receipt: null })).toThrow();
    expect(() =>
      decodeEngagementCreationState(
        { ...state, clientId: '00000000-0000-0000-0000-000000000000' },
        '',
      ),
    ).toThrow();
  });
});
describe('Native engagement creation review, tab recovery and ownership', () => {
  let ids: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    sessionStorage.clear();
    ids = new BehaviorSubject(convertToParamMap({ id }));
    TestBed.configureTestingModule({
      imports: [EngagementCreate],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: ids,
            snapshot: {
              paramMap: ids.value,
              queryParamMap: convertToParamMap({ contactPage: '2', contactPageSize: '25' }),
            },
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
    const f = TestBed.createComponent(EngagementCreate);
    f.detectChanges();
    TestBed.tick();
    return f;
  }
  function read(value = state) {
    const requests = TestBed.inject(HttpTestingController)
      .match((r) => r.method === 'GET' && !r.url.includes('/receipts/'))
      .filter((r) => !r.cancelled);
    expect(requests).toHaveLength(1);
    requests[0].flush(value);
    TestBed.tick();
  }
  async function preview(c: EngagementCreate) {
    c.model.set({ ...fields });
    TestBed.tick();
    const pending = c.prepare();
    const r = TestBed.inject(HttpTestingController).expectOne(url + '/preview');
    const body = r.request.body;
    r.flush({
      value: {
        clientId: id,
        requestId: body.requestId,
        reviewBasis: basis,
        requestHash: hash,
        fields,
      },
    });
    await pending;
    TestBed.tick();
    return body;
  }
  function receipt(requestId: string) {
    return {
      id: other,
      clientId: id,
      engagementId: other,
      actorId: id,
      requestId,
      requestHash: hash,
      reviewBasis: basis,
      clientGeneration: '9007199254740993',
      engagementGeneration: '1',
      fields,
      createdAt: '2026-10-03T12:30:00Z',
    };
  }
  it('shows exact blocked-shell intent, requires assent and invalidates review on edit', async () => {
    const f = open();
    read();
    const c = f.componentInstance;
    expect(c.returnParams.contactPage).toBe(2);
    expect(c.returnParams.contactPageSize).toBe(25);
    expect(
      f.nativeElement.querySelector('a[href*="/app/clients/"]').getAttribute('href'),
    ).toContain('contactPage=2');
    c.model.set({ ...fields, periodStart: 'bad' });
    TestBed.tick();
    await c.prepare();
    expect(c.preview()).toBeNull();
    await preview(c);
    expect(f.nativeElement.textContent).toContain('SYNTHETIC');
    await c.execute();
    TestBed.inject(HttpTestingController).expectNone(url);
    c.reviewed.set(true);
    c.model.update((v) => ({ ...v, serviceProfile: 'Changed' }));
    TestBed.tick();
    expect(c.preview()).toBeNull();
    expect(c.reviewed()).toBe(false);
  });
  it('dispatches once, blocks unknown resubmission and reconciles its actor-owned receipt', async () => {
    const f = open();
    read();
    const c = f.componentInstance,
      r = await preview(c);
    c.reviewed.set(true);
    const result = c.execute();
    const post = TestBed.inject(HttpTestingController).expectOne(url);
    expect(post.request.body.expectedRequestHash).toBe(hash);
    post.flush({}, { status: 503, statusText: 'Unavailable' });
    await result;
    TestBed.tick();
    expect(c.uncertain()).toBe(true);
    expect(await c.confirmNavigation()).toBe(false);
    await c.prepare();
    TestBed.inject(HttpTestingController).expectNone(url + '/preview');
    const check = c.reconcile();
    TestBed.inject(HttpTestingController)
      .expectOne(url + '/receipts/' + r.requestId + '?requestHash=' + hash)
      .flush({ found: true, receipt: receipt(r.requestId) });
    await check;
    TestBed.tick();
    expect(c.receipt()?.requestId).toBe(r.requestId);
    c.acknowledge();
    read({ ...state, reviewBasis: 'c'.repeat(64) });
    expect(c.uncertain()).toBe(false);
    expect(c.model().serviceProfile).toBe('');
    expect(c.pending()).toBeNull();
  });
  it('recovers only the request across changed revisions, never old editable fields or assent', async () => {
    const first = open();
    read();
    const c = first.componentInstance,
      r = await preview(c);
    c.reviewed.set(true);
    const dispatch = c.execute();
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({}, { status: 503, statusText: 'Unknown' });
    await dispatch;
    TestBed.tick();
    first.destroy();
    const next = open();
    read({ ...state, reviewBasis: 'c'.repeat(64) });
    const restored = next.componentInstance;
    expect(restored.uncertain()).toBe(true);
    expect(restored.pending()?.requestId).toBe(r.requestId);
    expect(restored.model().serviceProfile).toBe('');
    expect(restored.reviewed()).toBe(false);
    expect(restored.draftAvailable()).toBe(false);
    const check = restored.reconcile();
    TestBed.inject(HttpTestingController)
      .expectOne(url + '/receipts/' + r.requestId + '?requestHash=' + hash)
      .flush({ found: false, receipt: null });
    await check;
    TestBed.tick();
    expect(restored.absent()).toBe(true);
    restored.model.set({ ...fields, serviceProfile: 'Different' });
    TestBed.tick();
    const prepare = restored.prepare();
    const p = TestBed.inject(HttpTestingController).expectOne(url + '/preview');
    expect(p.request.body.requestId).toBe(r.requestId);
    p.flush({
      value: {
        clientId: id,
        requestId: r.requestId,
        reviewBasis: 'c'.repeat(64),
        requestHash: 'd'.repeat(64),
        fields: { ...fields, serviceProfile: 'Different' },
      },
    });
    await prepare;
    TestBed.tick();
    expect(restored.preview()).toBeNull();
    expect(restored.pending()).not.toBeNull();
  });
  it('restores matching tab fields explicitly and excludes review assent', () => {
    const f = open();
    read();
    const c = f.componentInstance;
    c.model.set(fields);
    TestBed.tick();
    c.reviewed.set(true);
    expect(c.saveDraft()).toBe(true);
    f.destroy();
    const next = open();
    read();
    expect(next.componentInstance.draftAvailable()).toBe(true);
    expect(next.componentInstance.model().serviceProfile).toBe('');
    next.componentInstance.restoreDraft();
    TestBed.tick();
    expect(next.componentInstance.model()).toEqual(fields);
    expect(next.componentInstance.reviewed()).toBe(false);
    expect(
      Array.from({ length: sessionStorage.length }, (_, i) =>
        sessionStorage.getItem(sessionStorage.key(i)!),
      ).join(),
    ).not.toContain('reviewed');
  });
  it('fences old route callbacks and removes local fields and receipts on session loss', async () => {
    const f = open();
    read();
    const c = f.componentInstance,
      r = await preview(c);
    c.reviewed.set(true);
    const dispatch = c.execute();
    const pending = TestBed.inject(HttpTestingController).expectOne(url);
    ids.next(convertToParamMap({ id: other }));
    TestBed.tick();
    read({ ...state, clientId: other });
    ids.next(convertToParamMap({ id }));
    TestBed.tick();
    read();
    c.model.set({ ...fields, serviceProfile: 'New route' });
    TestBed.tick();
    pending.flush({ value: receipt(r.requestId) });
    await dispatch;
    TestBed.tick();
    expect(c.model().serviceProfile).toBe('New route');
    expect(c.receipt()).toBeNull();
    TestBed.inject(SessionService).clear();
    TestBed.tick();
    expect(c.model().serviceProfile).toBe('');
    expect(c.state.data()).toBeNull();
    expect(sessionStorage.length).toBe(0);
  });
  it('keeps malformed successful acknowledgments unknown and fences destroyed callbacks', async () => {
    const f = open();
    read();
    const c = f.componentInstance;
    await preview(c);
    c.reviewed.set(true);
    const dispatched = c.execute();
    TestBed.inject(HttpTestingController).expectOne(url).flush({ value: {} });
    await dispatched;
    TestBed.tick();
    expect(c.uncertain()).toBe(true);
    expect(c.model()).toEqual(fields);
    expect(c.receipt()).toBeNull();
    await c.execute();
    TestBed.inject(HttpTestingController).expectNone(url);
    const check = c.reconcile();
    const reply = TestBed.inject(HttpTestingController).expectOne((r) =>
      r.url.includes('/receipts/'),
    );
    f.destroy();
    reply.flush({ found: false, receipt: null });
    await check;
    expect(c.absent()).toBe(false);
  });
  it('requires a fresh review for an absent identical retry and clears protected fields on scope refusal', async () => {
    const f = open();
    read();
    const c = f.componentInstance,
      r = await preview(c);
    c.reviewed.set(true);
    const dispatched = c.execute();
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({}, { status: 503, statusText: 'Unknown' });
    await dispatched;
    TestBed.tick();
    const check = c.reconcile();
    TestBed.inject(HttpTestingController)
      .expectOne(url + '/receipts/' + r.requestId + '?requestHash=' + hash)
      .flush({ found: false, receipt: null });
    await check;
    TestBed.tick();
    expect(c.absent()).toBe(true);
    const review = c.prepare();
    const p = TestBed.inject(HttpTestingController).expectOne(url + '/preview');
    expect(p.request.body.requestId).toBe(r.requestId);
    p.flush({
      value: {
        clientId: id,
        requestId: r.requestId,
        reviewBasis: basis,
        requestHash: hash,
        fields,
      },
    });
    await review;
    TestBed.tick();
    expect(c.reviewed()).toBe(false);
    await c.execute();
    TestBed.inject(HttpTestingController).expectNone(url);
    c.reviewed.set(true);
    const retry = c.execute();
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({ value: receipt(r.requestId) });
    await retry;
    TestBed.tick();
    read();
    expect(c.uncertain()).toBe(false);
    expect(c.receipt()).not.toBeNull();
    c.state.reload();
    TestBed.tick();
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({}, { status: 403, statusText: 'Forbidden' });
    TestBed.tick();
    expect(c.model().serviceProfile).toBe('');
    expect(c.preview()).toBeNull();
    expect(c.receipt()).toBeNull();
  });
  it('removes protected context immediately when reviewed creation is denied', async () => {
    const f = open();
    read();
    const c = f.componentInstance;
    await preview(c);
    c.reviewed.set(true);
    const dispatched = c.execute();
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({ code: 'scope.denied' }, { status: 403, statusText: 'Forbidden' });
    await dispatched;
    TestBed.tick();
    expect(c.state.data()).toBeNull();
    expect(c.model().serviceProfile).toBe('');
    expect(c.preview()).toBeNull();
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({}, { status: 403, statusText: 'Forbidden' });
    TestBed.tick();
    expect(c.receipt()).toBeNull();
  });
});
