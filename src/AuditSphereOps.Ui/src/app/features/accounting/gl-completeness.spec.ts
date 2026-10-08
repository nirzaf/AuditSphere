import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { GeneralLedgerCompleteness } from './gl-completeness';
import {
  decodeCompletenessDraft,
  decodeCompletenessPlan,
  decodeCompletenessReview,
  decodeCompletenessWorkspace,
} from './gl-completeness-contracts';
const id = '11111111-1111-4111-8111-111111111111',
  tb = '22222222-2222-4222-8222-222222222222',
  opening = '33333333-3333-4333-8333-333333333333',
  bridge = '44444444-4444-4444-8444-444444444444',
  other = '55555555-5555-4555-8555-555555555555';
const h = 'a'.repeat(64),
  revision = 'b'.repeat(64),
  stamp = '2026-01-01T00:00:00Z';
const context = {
  source: {
    batchId: id,
    clientId: id,
    engagementId: id,
    periodId: id,
    periodCode: '2026',
    bookId: id,
    entity: 'SYN-COMPLETE',
    currency: 'QAR',
    importState: 'SEALED',
    rawFileSha256: h,
    sourceHash: h,
    profileVersion: 'synthetic',
    parserVersion: 'v1',
    importedByUserId: other,
    importedAt: stamp,
    selected: null,
  },
  periodStatus: 'ACTIVE',
  basis: 'IFRS',
  startDate: '2026-01-01',
  endDate: '2026-12-31',
  priorPeriodId: opening,
  bookCode: 'STAT',
  inputGeneration: 1,
  selectedTrialBalance: null,
  revision,
  canPrepare: true,
  blocker: null,
};
const closing = {
  id: tb,
  periodId: id,
  periodCode: '2026',
  bookId: id,
  revision: 1,
  sourceHash: h,
  importedAt: stamp,
};
const prior = { ...closing, id: opening, periodId: opening, periodCode: '2025', bookId: opening };
const workspace = {
  context,
  closingSources: { items: [closing], totalCount: 1, page: 1, pageSize: 20 },
  openingSources: { items: [prior], totalCount: 1, page: 1, pageSize: 20 },
  bridges: [],
  moreBridges: false,
};
const plan = {
  context,
  closing,
  opening: prior,
  existingBridgeId: null,
  operations: [],
  revision: h,
  canPrepare: true,
  blocker: null,
};
const review = {
  context,
  bridge: {
    id: bridge,
    firmId: id,
    clientId: id,
    engagementId: id,
    periodId: id,
    bookId: id,
    trialBalanceDatasetId: tb,
    importBatchId: id,
    openingTrialBalanceDatasetId: opening,
    trialBalanceHash: h,
    generalLedgerHash: h,
    openingTrialBalanceHash: h,
    accountResidualDigest: h,
    openingMovementResidualDigest: h,
    trialBalanceAccountCount: 2,
    generalLedgerAccountCount: 2,
    matchedAccountCount: 2,
    mismatchedAccountCount: 0,
    openingMovementMismatchedAccountCount: 0,
    journalExceptionCount: 0,
    openingAmount: '0',
    movementAmount: '0',
    closingAmount: '0',
    openingMovementResidual: '0',
    absoluteResidual: '0',
    coverageStart: '2026-06-30',
    coverageEnd: '2026-06-30',
    status: 'RECONCILED',
    evidenceReference: 'Synthetic retained evidence',
    incompleteExtract: false,
    completenessDisclosure: '',
    createdByUserId: other,
    createdAt: stamp,
    reviewedByUserId: null,
    reviewedAt: null,
  },
  residuals: {
    items: [
      {
        accountCode: '1000',
        closing: '100.123456',
        movement: '100.123456',
        opening: '0',
        movementClosingDifference: '0',
        openingMovementDifference: '0',
      },
    ],
    totalCount: 1,
    page: 1,
    pageSize: 50,
  },
  revision: h,
  canApprove: true,
  canReject: true,
  approvalBlocker: null,
  reviewBlocker: null,
};
const url = `/api/ui/gl-sources/${id}/completeness`,
  list = url + '?page=1&openingPage=1',
  pair = url + `/plan?trialBalanceId=${tb}&openingId=${opening}`,
  detail = `/api/ui/gl-completeness/${bridge}?page=1`;
describe('Native GL completeness', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [GeneralLedgerCompleteness],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: new BehaviorSubject(convertToParamMap({ id })) },
        },
      ],
    });
    TestBed.inject(SessionService).current.set({
      userId: id,
      firmId: id,
      generation: '0',
      staff: true,
    });
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });
  async function open() {
    const f = TestBed.createComponent(GeneralLedgerCompleteness);
    f.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(list).flush(workspace);
    await f.whenStable();
    TestBed.tick();
    f.detectChanges();
    return { f, c: f.componentInstance, http };
  }
  async function prepare(c: GeneralLedgerCompleteness, http: HttpTestingController) {
    c.edit('trialBalanceId', tb);
    c.edit('openingId', opening);
    const load = c.loadPlan();
    http.expectOne(pair).flush(plan);
    await load;
    c.edit('evidenceReference', 'Reviewed source evidence');
    c.recordAssent({ target: { checked: true } } as unknown as Event);
  }
  it('prepares once with fresh bound assent and observes a durable operation without claiming approval', async () => {
    const { f, c, http } = await open();
    await prepare(c, http);
    expect(c.canSubmit()).toBe(true);
    c.edit('evidenceReference', 'Changed evidence');
    expect(c.canSubmit()).toBe(false);
    await c.submit();
    http.expectNone((r) => r.method === 'POST');
    c.recordAssent({ target: { checked: true } } as unknown as Event);
    const pending = c.submit();
    const write = http.expectOne(url);
    expect(write.request.body).toEqual({
      trialBalanceId: tb,
      openingId: opening,
      revision: h,
      evidenceReference: 'Changed evidence',
      reviewed: true,
    });
    write.flush({ value: other });
    await Promise.resolve();
    await Promise.resolve();
    http.expectOne(list).flush(workspace);
    await pending;
    expect(c.message()).toContain('queued');
    expect(c.review()).toBeNull();
    f.destroy();
  });
  it('retains an unknown preparation checkpoint across reload and requires explicit persisted-state acknowledgement', async () => {
    const first = await open();
    await prepare(first.c, first.http);
    const pending = first.c.submit();
    first.http.expectOne(url).flush({}, { status: 503, statusText: 'Unavailable' });
    await pending;
    expect(first.c.uncertain()).toBe(true);
    first.f.destroy();
    const { f, c, http } = await open();
    expect(c.uncertain()).toBe(true);
    expect(c.reviewed()).toBe(false);
    expect(await c.confirmNavigation()).toBe(false);
    c.acknowledge();
    expect(c.uncertain()).toBe(true);
    const read = c.refresh(true);
    http.expectOne(list).flush(workspace);
    await Promise.resolve();
    await Promise.resolve();
    http.expectOne(pair).flush({
      ...plan,
      canPrepare: false,
      blocker: 'Prior operation requires recovery.',
      operations: [
        {
          id: other,
          status: 'QUEUED',
          originatorId: id,
          evidenceReference: 'Reviewed source evidence',
          openingTrialBalanceDatasetId: opening,
          resultIdentity: null,
          createdAt: stamp,
        },
      ],
    });
    await read;
    c.acknowledge();
    expect(c.uncertain()).toBe(false);
    expect(c.canSubmit()).toBe(false);
    http.expectNone((r) => r.method === 'POST');
    f.destroy();
  });
  it('reviews real exact residuals, rejects stale identity and removes protected content after invalidation', async () => {
    const { f, c, http } = await open();
    const load = c.loadReview(bridge);
    http.expectOne(detail).flush(review);
    await load;
    TestBed.tick();
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('100.123456');
    expect(f.nativeElement.textContent).toContain('Independent completeness review');
    c.recordAssent({ target: { checked: true } } as unknown as Event);
    expect(c.canSubmit()).toBe(true);
    const read = c.loadReview(bridge);
    expect(c.review()).toBeNull();
    http.expectOne(detail).flush({ ...review, bridge: { ...review.bridge, firmId: other } });
    await read;
    expect(c.review()).toBeNull();
    expect(c.reviewed()).toBe(false);
    const retry = c.loadReview(bridge);
    http.expectOne(detail).flush(review);
    await retry;
    TestBed.inject(SessionService).current.set(null);
    TestBed.inject(SessionService).invalidation.update((x) => x + 1);
    TestBed.tick();
    expect(c.workspace()).toBeNull();
    expect(c.review()).toBeNull();
    expect(c.model().evidenceReference).toBe('');
    f.destroy();
  });
  it('records a reviewer decision once and preserves the unknown outcome fence on reload', async () => {
    const first = await open();
    const load = first.c.loadReview(bridge);
    first.http.expectOne(detail).flush(review);
    await load;
    first.c.recordAssent({ target: { checked: true } } as unknown as Event);
    const pending = first.c.submit();
    const write = first.http.expectOne(`/api/ui/gl-completeness/${bridge}/review`);
    expect(write.request.body).toEqual({ revision: h, approve: true, reviewed: true });
    write.flush({}, { status: 503, statusText: 'Unavailable' });
    await pending;
    first.f.destroy();
    const { f, c, http } = await open();
    expect(c.uncertain()).toBe(true);
    const read = c.refresh(true);
    http.expectOne(list).flush(workspace);
    await Promise.resolve();
    await Promise.resolve();
    http.expectOne(detail).flush({
      ...review,
      bridge: { ...review.bridge, status: 'APPROVED', reviewedByUserId: id, reviewedAt: stamp },
      canApprove: false,
      canReject: false,
      approvalBlocker: 'Already reviewed.',
      reviewBlocker: 'Already reviewed.',
    });
    await read;
    c.acknowledge();
    expect(c.review()?.bridge.status).toBe('APPROVED');
    expect(c.canSubmit()).toBe(false);
    http.expectNone((r) => r.method === 'POST');
    f.destroy();
  });
  it('visibly clears assent when a native check and decision edit share one render turn', async () => {
    const { f, c, http } = await open();
    const load = c.loadReview(bridge);
    http.expectOne(detail).flush(review);
    await load;
    f.detectChanges();
    const checkbox = f.nativeElement.querySelector('input[type="checkbox"]') as HTMLInputElement;
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change', { bubbles: true }));
    expect(c.reviewed()).toBe(true);
    // Deliberately do not render the checked signal before the next real input event.
    const decision = f.nativeElement.querySelector(
      'select[aria-label="Completeness review decision"]',
    ) as HTMLSelectElement;
    decision.value = 'reject';
    decision.dispatchEvent(new Event('change', { bubbles: true }));
    f.detectChanges();
    const fresh = f.nativeElement.querySelector('input[type="checkbox"]') as HTMLInputElement;
    expect(fresh).not.toBe(checkbox);
    expect(fresh.checked).toBe(false);
    expect(c.reviewed()).toBe(false);
    expect(c.canSubmit()).toBe(false);
    f.destroy();
  });
  it('represents missing opening as Unknown and rejects forged approval, mismatched pairs and unbounded contracts', async () => {
    const { f, c, http } = await open();
    const loading = c.loadReview(bridge);
    http.expectOne(detail).flush({
      ...review,
      bridge: {
        ...review.bridge,
        incompleteExtract: true,
        completenessDisclosure: 'OPENING_ACCOUNT_COVERAGE_MISSING',
      },
      canApprove: false,
      approvalBlocker: 'Missing opening coverage.',
      residuals: {
        ...review.residuals,
        items: [{ ...review.residuals.items[0], opening: null, openingMovementDifference: null }],
      },
    });
    await loading;
    TestBed.tick();
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Unknown');
    c.recordAssent({ target: { checked: true } } as unknown as Event);
    expect(c.canSubmit()).toBe(false);
    c.edit('action', 'reject');
    c.recordAssent({ target: { checked: true } } as unknown as Event);
    expect(c.canSubmit()).toBe(true);
    f.destroy();
    expect(() =>
      decodeCompletenessReview({
        ...review,
        bridge: { ...review.bridge, incompleteExtract: true },
      }),
    ).toThrow();
    expect(() =>
      decodeCompletenessReview({ ...review, residuals: { ...review.residuals, pageSize: 500 } }),
    ).toThrow();
    expect(() =>
      decodeCompletenessPlan({ ...plan, closing: { ...closing, periodId: other } }),
    ).toThrow();
    expect(() =>
      decodeCompletenessWorkspace({
        ...workspace,
        closingSources: { ...workspace.closingSources, items: Array(21).fill(closing) },
      }),
    ).toThrow();
    expect(
      decodeCompletenessDraft({
        ...{
          trialBalanceId: tb,
          openingId: opening,
          bridgeId: '',
          action: 'prepare',
          evidenceReference: 'draft',
        },
        reviewed: true,
      }),
    ).toBeNull();
  });
});
