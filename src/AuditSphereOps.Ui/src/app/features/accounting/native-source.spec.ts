import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { NativeSourcePage } from './native-source';
import { nativeSourceBase } from './native-source-api';
import { NATIVE_BOOK_SCOPE, NativeSourceIntent, decodeNativeArtifact, decodeNativeContext,
  decodeNativePreview, decodeNativeSource, nativePendingIntent } from './native-source-contracts';

const client = '11111111-1111-4111-8111-111111111111', engagement = '22222222-2222-4222-8222-222222222222',
  periodId = '33333333-3333-4333-8333-333333333333', actor = '44444444-4444-4444-8444-444444444444',
  dataset = '55555555-5555-4555-8555-555555555555', requestId = '66666666-6666-4666-8666-666666666666';
const period = { id: periodId, revision: '9007199254740993', code: 'FY26', start: '2026-01-01', end: '2026-12-31',
  basis: 'IFRS', currency: 'QAR', status: 'ACTIVE' };
const context = { clientId: client, name: 'Synthetic scoped client', profile: { sourceMode: 'NATIVE_BOOKKEEPING', currency: 'QAR' },
  periods: [period], hasMorePeriods: false };
const fields = { periodId, engagementId: engagement, asOfDate: period.end, legalEntityKey: 'SYNTHETIC-ENTITY',
  evidenceReference: 'SYNTHETIC-NATIVE-SOURCE', allowActivityOnly: false };
const intent: NativeSourceIntent = { ...fields, clientId: client, expectedPeriodRevision: period.revision, requestId, reviewBasis: 'a'.repeat(64) };
const exact = '9007199254740.123456';
const preview = {
  reviewBasis: 'a'.repeat(64), sourceIdentityHash: 'b'.repeat(64), manifestHash: 'c'.repeat(64), generatedRowsHash: 'd'.repeat(64),
  rowCount: 2, bookScope: NATIVE_BOOK_SCOPE, activityOnly: false, currency: 'QAR', postingSequence: '9007199254740994',
  trialBalance: { fromDate: period.start, toDate: period.end, source: 'Synthetic native ledger',
    openingDebit: '0.000000', openingCredit: '0.000000', periodDebit: exact, periodCredit: exact, closingDebit: exact, closingCredit: exact,
    rows: [{ accountId: client, accountCode: '1000', accountName: 'Cash', openingDebit: '0.000000', openingCredit: '0.000000',
      periodDebit: exact, periodCredit: '0.000000', closingDebit: exact, closingCredit: '0.000000' },
    { accountId: engagement, accountCode: '3000', accountName: 'Capital', openingDebit: '0.000000', openingCredit: '0.000000',
      periodDebit: '0.000000', periodCredit: exact, closingDebit: '0.000000', closingCredit: exact }] },
};
const source = { datasetId: dataset, clientId: client, engagementId: engagement, periodId, asOfDate: period.end,
  legalEntityKey: fields.legalEntityKey, bookScope: NATIVE_BOOK_SCOPE, sourceKind: 'Native', activityOnly: false,
  rowCount: 2, sourceIdentityHash: preview.sourceIdentityHash, manifestHash: preview.manifestHash,
  generatedRowsHash: preview.generatedRowsHash, validationStatus: 'Pending', isCurrent: true, currentnessBlocker: null,
  capturedByUserId: actor, createdAt: '2026-10-24T10:00:00Z' };
const root = nativeSourceBase(client);

describe('native trial balance source adapter', () => {
  const params = new BehaviorSubject(convertToParamMap({ clientId: client }));
  beforeEach(() => {
    sessionStorage.clear(); params.next(convertToParamMap({ clientId: client }));
    TestBed.configureTestingModule({ imports: [NativeSourcePage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { paramMap: params, queryParamMap: new BehaviorSubject(convertToParamMap({ periodId })) } }] });
    TestBed.inject(SessionService).current.set({ userId: actor, firmId: client, generation: '1', staff: true });
  });
  afterEach(() => { TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); sessionStorage.clear(); });
  async function open() {
    const f = TestBed.createComponent(NativeSourcePage); f.detectChanges(); TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(`/api/ui/accounting/clients/${client}`).flush(context);
    await f.whenStable(); TestBed.tick();
    const c = f.componentInstance; c.model.set({ ...fields }); TestBed.tick();
    return { f, c, http };
  }
  async function prepare(c: NativeSourcePage, http: HttpTestingController, value = preview) {
    c.wholePeriod.set(true); TestBed.tick();
    const task = c.makePreview();
    http.expectOne(`/api/ui/engagements/${engagement}/tb-intake`).flush({ engagementId: engagement, clientId: client, datasets: [] });
    await Promise.resolve(); await Promise.resolve();
    const r = http.expectOne(root + '/preview'); const body = r.request.body;
    r.flush({ value }); await task; TestBed.tick(); return body;
  }
  it('preserves exact money, revisions and posting sequence and rejects unsupported or mismatched scope', () => {
    expect(decodeNativeContext(context, client).periods[0].revision).toBe('9007199254740993');
    expect(decodeNativePreview(preview, period, intent).trialBalance.periodDebit).toBe(exact);
    expect(decodeNativePreview(preview, period, intent).postingSequence).toBe('9007199254740994');
    expect(() => decodeNativePreview({ ...preview, postingSequence: 9007199254740994 }, period, intent)).toThrow();
    expect(() => decodeNativePreview({ ...preview, bookScope: 'AUDIT_BOOK' }, period, intent)).toThrow();
    expect(() => decodeNativePreview({ ...preview, trialBalance: { ...preview.trialBalance, closingDebit: 1 } }, period, intent)).toThrow();
    expect(() => decodeNativePreview({ ...preview, activityOnly: true }, period, intent)).toThrow();
    expect(() => decodeNativePreview(preview, period, { ...intent, asOfDate: '2026-11-30' })).toThrow();
    expect(() => decodeNativeSource({ ...source, clientId: engagement }, client)).toThrow();
    expect(() => decodeNativeContext({ ...context, profile: { ...context.profile, sourceMode: 'EXTERNAL_SOURCE' } }, client)).toThrow();
    expect(nativePendingIntent({ ...intent, reviewed: true })).toBeNull();
    const s = decodeNativeSource(source, client);
    expect(() => decodeNativeArtifact({ ...s, fileName: `auditsphere-native-tb-${dataset}.json`, contentType: 'application/json',
      generatedRowsJson: '[]', manifestJson: '{}', manifestHash: 'e'.repeat(64) }, s)).toThrow();
  });
  it('requires explicit no-book selection, exact preview and fresh acknowledgment before sending one seal', async () => {
    const { f, c, http } = await open();
    await c.makePreview(); http.expectNone(`/api/ui/engagements/${engagement}/tb-intake`);
    const body = await prepare(c, http);
    expect(body).toMatchObject({ clientId: client, engagementId: engagement, periodId,
      expectedPeriodRevision: period.revision, asOfDate: period.end, legalEntityKey: fields.legalEntityKey,
      evidenceReference: fields.evidenceReference, periodLedgerReviewed: true, reviewed: false, reviewBasis: null });
    expect(body.bookId).toBeUndefined();
    await c.seal(); http.expectNone(root + '/seal');
    c.reviewed.set(true); const task = c.seal(); await c.seal();
    const seal = http.expectOne(root + '/seal');
    expect(seal.request.body).toEqual({ ...body, reviewBasis: preview.reviewBasis, reviewed: true });
    seal.flush({ value: source }); await task; TestBed.tick(); f.detectChanges();
    expect(c.displaySource()?.datasetId).toBe(dataset);
    expect(f.nativeElement.textContent).toContain('not an uploaded file');
    expect(f.nativeElement.textContent).toContain('Native GL completeness and period continuity');
    http.expectNone('/api/ui/accounting/sources/' + dataset + '/acceptance');
  });
  it('does not send a seal after an immediate unreviewed field edit', async () => {
    const { c, http } = await open(); await prepare(c, http); c.reviewed.set(true);
    c.model.update(m => ({ ...m, evidenceReference: 'CHANGED-NOT-REVIEWED' }));
    await c.seal(); http.expectNone(root + '/seal'); TestBed.tick();
    expect(c.preview()).toBeNull(); expect(c.reviewed()).toBe(false);
  });
  it('refuses sibling engagement context before preview and discards a preview after session revocation', async () => {
    const { c, http } = await open(); c.wholePeriod.set(true); TestBed.tick();
    let task = c.makePreview();
    http.expectOne(`/api/ui/engagements/${engagement}/tb-intake`).flush({ engagementId: engagement, clientId: periodId, datasets: [] });
    await task; expect(c.preview()).toBeNull(); http.expectNone(root + '/preview');
    task = c.makePreview();
    http.expectOne(`/api/ui/engagements/${engagement}/tb-intake`).flush({ engagementId: engagement, clientId: client, datasets: [] });
    await Promise.resolve(); await Promise.resolve(); const r = http.expectOne(root + '/preview');
    TestBed.inject(SessionService).invalidation.update(n => n + 1); TestBed.inject(SessionService).current.set(null); TestBed.tick();
    r.flush({ value: preview }); await task; TestBed.tick();
    expect(c.preview()).toBeNull(); expect(c.displaySource()).toBeNull(); expect(c.canSeal()).toBe(false);
  });
  it('retains an unknown seal identity across reload and requires renewed acknowledgment for its exact explicit retry', async () => {
    let opened = await open(); const original = await prepare(opened.c, opened.http);
    opened.c.reviewed.set(true); const send = opened.c.seal();
    opened.http.expectOne(root + '/seal').flush({}, { status: 503, statusText: 'Unavailable' }); await send;
    expect(opened.c.uncertain()).toBe(true); expect(opened.c.reviewed()).toBe(false);
    opened.http.expectNone(root + '/seal'); opened.f.destroy();
    opened = await open();
    expect(opened.c.pending()?.requestId).toBe(original.requestId);
    await opened.c.seal(); opened.http.expectNone(root + '/seal');
    opened.c.wholePeriod.set(true); opened.c.reviewed.set(true); const retry = opened.c.seal();
    const request = opened.http.expectOne(root + '/seal');
    expect(request.request.body.requestId).toBe(original.requestId);
    expect(request.request.body.expectedPeriodRevision).toBe(original.expectedPeriodRevision);
    expect(request.request.body.reviewBasis).toBe(preview.reviewBasis);
    request.flush({ value: source }); await retry;
    expect(opened.c.uncertain()).toBe(false); expect(opened.c.pending()).toBeNull();
  });
  it('pages the complete bounded population before sealing and labels activity-only evidence as blocked', async () => {
    const { f, c, http } = await open();
    const rows = Array.from({ length: 202 }, (_, i) => ({ ...preview.trialBalance.rows[i % 2],
      accountId: `00000000-0000-4000-8000-${(i + 1).toString(16).padStart(12, '0')}`, accountCode: String(1000 + i),
      periodDebit: i % 2 === 0 ? '100.000000' : '0.000000', periodCredit: i % 2 === 1 ? '100.000000' : '0.000000',
      closingDebit: i % 2 === 0 ? '100.000000' : '0.000000', closingCredit: i % 2 === 1 ? '100.000000' : '0.000000' }));
    const population = { ...preview, rowCount: rows.length, activityOnly: true, trialBalance: { ...preview.trialBalance,
      periodDebit: '10100.000000', periodCredit: '10100.000000', closingDebit: '10100.000000', closingCredit: '10100.000000', rows } };
    c.model.update(m => ({ ...m, allowActivityOnly: true })); TestBed.tick(); await prepare(c, http, population); f.detectChanges();
    expect(c.visibleRows()).toHaveLength(100); c.rowPage.set(2); f.detectChanges();
    expect(c.visibleRows()).toHaveLength(2); expect(c.visibleRows()[1].accountCode).toBe('1201');
    expect(f.nativeElement.textContent).toContain('Full report acceptance is blocked');
    expect(f.nativeElement.textContent).toContain('10100.000000');
    expect(f.nativeElement.querySelector('mat-paginator')).toBeTruthy();
  });
});
