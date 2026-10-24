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
    originKind: 'EXTERNAL',
    rawFileSha256: h,
    sourceHash: h,
    profileVersion: 'synthetic',
    parserVersion: 'v1',
    importedByUserId: other,
    importedAt: stamp,
    selected: null,
  },
  periodStatus: 'ACTIVE',
  nativeOpening: null,
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
const nativeContext = {
  ...context,
  source: {
    ...context.source,
    originKind: 'NATIVE_LEDGER',
    bookId: null,
    rawFileSha256: '',
    profileVersion: 'native-posted-gl.v1',
    parserVersion: 'native-gl-export.v1',
  },
  nativeOpening: { openingId: opening, manifestHash: h },
  bookCode: null,
  priorPeriodId: null,
};
const nativeClosing = { ...closing, bookId: null };
const nativeWorkspace = {
  ...workspace,
  context: nativeContext,
  closingSources: { ...workspace.closingSources, items: [nativeClosing] },
  openingSources: { ...workspace.openingSources, items: [], totalCount: 0 },
};
const nativePlan = { ...plan, context: nativeContext, closing: nativeClosing, opening: null };
const nativeReview = {
  ...review,
  context: nativeContext,
  bridge: {
    ...review.bridge,
    bookId: null,
    openingTrialBalanceDatasetId: null,
    openingTrialBalanceHash: h,
  },
  residuals: {
    ...review.residuals,
    items: [{ ...review.residuals.items[0], opening: '100', movement: '0.123456' }],
  },
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
  async function open(value: object = workspace) {
    const f = TestBed.createComponent(GeneralLedgerCompleteness);
    f.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(list).flush(value);
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
  it.each(['restore', 'moved', 'keyboard', 'stale'])(
    'returns keyboard refresh focus only for its current owner (%s)',
    async (action) => {
      const { f, c, http } = await open();
      f.detectChanges();
      const trigger = Array.from(
        f.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
      ).find((button) => button.textContent?.trim() === 'Refresh completeness')!;
      trigger.focus();
      const pending = c.refresh(),
        read = http.expectOne(list);
      f.detectChanges();
      expect(trigger.disabled).toBe(true);
      const elsewhere = f.nativeElement.querySelector(
        'a[href="/app/operations"]',
      ) as HTMLAnchorElement;
      // jsdom cannot blur its already-disabled active button. Establish the
      // observed browser body-focus state through a programmatic focus/blur,
      // without synthesizing user keyboard/pointer interaction.
      elsewhere.focus();
      elsewhere.blur();
      expect(document.activeElement).toBe(document.body);
      if (action === 'moved') elsewhere.focus();
      if (action === 'keyboard')
        document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
      if (action === 'stale') {
        TestBed.inject(SessionService).current.set(null);
        TestBed.inject(SessionService).invalidation.update((n) => n + 1);
        TestBed.tick();
      }
      read.flush(workspace);
      await pending;
      await f.whenStable();
      TestBed.tick();
      f.detectChanges();
      expect(document.activeElement).toBe(
        action === 'restore' ? trigger : action === 'moved' ? elsewhere : document.body,
      );
      if (action === 'restore') expect(trigger.disabled).toBe(false);
      if (action === 'stale') expect(c.workspace()).toBeNull();
      f.destroy();
    },
  );
  it('uses the inherited native opening without an imported selector and preserves preparation and independent review assent', async () => {
    expect(decodeCompletenessWorkspace(nativeWorkspace).context.nativeOpening).toEqual(
      nativeContext.nativeOpening,
    );
    const { f, c, http } = await open(nativeWorkspace);
    expect(c.workspace(), c.error()).not.toBeNull();
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('no reporting book');
    expect(f.nativeElement.textContent).toContain(opening);
    expect(f.nativeElement.querySelector('select[aria-label="Opening trial balance"]')).toBeNull();
    c.edit('openingId', other);
    expect(c.model().openingId).toBe('');
    c.edit('trialBalanceId', tb);
    const loading = c.loadPlan();
    http.expectOne(url + `/plan?trialBalanceId=${tb}`).flush(nativePlan);
    await loading;
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Inherited native opening / manifest');
    expect(f.nativeElement.textContent).not.toContain('Unknown opening');
    c.edit('evidenceReference', 'Synthetic native completeness review');
    await c.submit();
    http.expectNone((r) => r.method === 'POST');
    c.recordAssent({ target: { checked: true } } as unknown as Event);
    const preparing = c.submit(),
      write = http.expectOne(url);
    expect(write.request.body).toEqual({
      trialBalanceId: tb,
      openingId: null,
      revision: h,
      evidenceReference: 'Synthetic native completeness review',
      reviewed: true,
    });
    write.flush({ value: other });
    await Promise.resolve();
    await Promise.resolve();
    http.expectOne(list).flush(nativeWorkspace);
    await preparing;
    expect(c.reviewed()).toBe(false);
    const read = c.loadReview(bridge);
    http.expectOne(detail).flush(nativeReview);
    await read;
    f.detectChanges();
    expect(f.nativeElement.textContent).not.toContain('Unknown opening');
    expect(f.nativeElement.textContent).toContain('100.123456');
    expect(c.canSubmit()).toBe(false);
    c.recordAssent({ target: { checked: true } } as unknown as Event);
    expect(c.canSubmit()).toBe(true);
    const approving = c.submit(),
      decision = http.expectOne(`/api/ui/gl-completeness/${bridge}/review`);
    expect(decision.request.body).toEqual({ revision: h, approve: true, reviewed: true });
    decision.flush({ value: true });
    await Promise.resolve();
    await Promise.resolve();
    http.expectOne(list).flush(nativeWorkspace);
    await Promise.resolve();
    await Promise.resolve();
    await vi.waitFor(() =>
      http.expectOne(detail).flush({
        ...nativeReview,
        bridge: {
          ...nativeReview.bridge,
          status: 'APPROVED',
          reviewedByUserId: id,
          reviewedAt: stamp,
        },
        canApprove: false,
        canReject: false,
        approvalBlocker: 'Already reviewed.',
        reviewBlocker: 'Already reviewed.',
      }),
    );
    await approving;
    expect(c.canSubmit()).toBe(false);
    expect(c.reviewed()).toBe(false);
    f.destroy();
  });
  it('refuses native missing or mismatched opening proof and preserves external raw-file strictness', async () => {
    for (const bad of [
      null,
      { openingId: 'invalid', manifestHash: h },
      { openingId: opening, manifestHash: '' },
    ]) {
      expect(() =>
        decodeCompletenessWorkspace({
          ...nativeWorkspace,
          context: { ...nativeContext, nativeOpening: bad },
        }),
      ).toThrow();
    }
    expect(() =>
      decodeCompletenessWorkspace({
        ...workspace,
        context: { ...context, source: { ...context.source, rawFileSha256: '' } },
      }),
    ).toThrow();
    expect(() =>
      decodeCompletenessWorkspace({
        ...nativeWorkspace,
        context: { ...nativeContext, source: { ...nativeContext.source, bookId: id } },
      }),
    ).toThrow();
    expect(() => decodeCompletenessPlan({ ...nativePlan, opening: prior })).toThrow();
    expect(() =>
      decodeCompletenessReview({
        ...nativeReview,
        bridge: { ...nativeReview.bridge, openingTrialBalanceHash: 'c'.repeat(64) },
      }),
    ).toThrow();
    expect(() =>
      decodeCompletenessReview({
        ...nativeReview,
        context: { ...nativeContext, nativeOpening: null },
      }),
    ).toThrow();
    const { f, c, http } = await open({
      ...nativeWorkspace,
      context: { ...nativeContext, nativeOpening: null },
    });
    expect(c.workspace()).toBeNull();
    expect(c.canSubmit()).toBe(false);
    await c.submit();
    http.expectNone((r) => r.method === 'POST');
    f.destroy();
  });
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
