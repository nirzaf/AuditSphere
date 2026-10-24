import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { NativeLedgerSourcePage } from './native-ledger-source';
import { nativeLedgerBase } from './native-ledger-source-api';
import { nativeSourceBase } from './native-source-api';
import { decodeNativeSource } from './native-source-contracts';
import {
  decodeNativeLedgerArtifact,
  decodeNativeLedgerPreview,
  decodeNativeLedgerSource,
  nativeLedgerPendingIntent,
} from './native-ledger-source-contracts';

const id = (n: number) => `00000000-0000-4000-8000-${n.toString(16).padStart(12, '0')}`;
const client = id(1),
  engagement = id(2),
  dataset = id(3),
  period = id(4),
  actor = id(5),
  batch = id(6);
const tb = {
  datasetId: dataset,
  clientId: client,
  engagementId: engagement,
  periodId: period,
  asOfDate: '2026-12-31',
  legalEntityKey: 'SYNTHETIC-ENTITY',
  bookScope: 'PERIOD_LEDGER_NO_BOOK',
  sourceKind: 'Native',
  activityOnly: false,
  rowCount: 3,
  sourceIdentityHash: 'a'.repeat(64),
  manifestHash: 'b'.repeat(64),
  generatedRowsHash: 'c'.repeat(64),
  validationStatus: 'Accepted',
  isCurrent: true,
  currentnessBlocker: null,
  capturedByUserId: actor,
  createdAt: '2026-10-24T10:00:00Z',
};
const exact = '1234567890123.123456';
const rows = Array.from({ length: 102 }, (_, i) => ({
  journalId: id(100 + Math.floor(i / 2)),
  lineId: id(1000 + i),
  clientAccountId: id(i % 2 === 0 ? 10 : 11),
  journalNumber: `SYN-${Math.floor(i / 2)}`,
  postingDate: '2026-06-30',
  accountCode: i % 2 === 0 ? '1000' : '2000',
  debit: i % 2 === 0 ? exact : '0',
  credit: i % 2 === 0 ? '0' : exact,
  originalCurrency: 'QAR',
  originalAmount: i % 2 === 0 ? exact : '-' + exact,
  functionalAmount: i % 2 === 0 ? exact : '-' + exact,
  currency: 'QAR',
  sourceUser: actor,
  sourceSystem: '',
  isManual: null,
  isYearEnd: null,
}));
const facts = {
  trialBalanceDatasetId: dataset,
  periodId: period,
  asOfDate: tb.asOfDate,
  currency: 'QAR',
  legalEntityKey: tb.legalEntityKey,
  bookScope: tb.bookScope,
  originKind: 'NATIVE_LEDGER',
  sourceIdentityHash: 'd'.repeat(64),
  manifestHash: 'e'.repeat(64),
  generatedRowsHash: 'f'.repeat(64),
  transactionCount: 51,
  lineCount: 102,
  totalDebit: '62962962396279.296256',
  totalCredit: '62962962396279.296256',
  postingSequence: '9007199254740993',
  nativeOpening: { openingId: id(7), manifestHash: '1'.repeat(64) },
  unavailableMetadata: ['Manual classification', 'Year-end classification', 'Source system'],
};
const preview = { ...facts, reviewBasis: '2'.repeat(64), rows };
const source = {
  ...facts,
  importBatchId: batch,
  clientId: client,
  engagementId: engagement,
  status: 'SEALED',
  isCurrent: true,
  currentnessBlocker: null,
  capturedByUserId: actor,
  createdAt: tb.createdAt,
};
const root = nativeLedgerBase(client),
  tbUrl = nativeSourceBase(client) + '/' + dataset;

describe('Native generated GL source adapter', () => {
  const params = new BehaviorSubject(convertToParamMap({ clientId: client, datasetId: dataset }));
  beforeEach(() => {
    sessionStorage.clear();
    params.next(convertToParamMap({ clientId: client, datasetId: dataset }));
    TestBed.configureTestingModule({
      imports: [NativeLedgerSourcePage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
      ],
    });
    TestBed.inject(SessionService).current.set({
      userId: actor,
      firmId: client,
      generation: '1',
      staff: true,
    });
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
    sessionStorage.clear();
  });
  async function open() {
    const f = TestBed.createComponent(NativeLedgerSourcePage);
    f.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(tbUrl).flush(tb);
    await f.whenStable();
    TestBed.tick();
    const c = f.componentInstance;
    if (!c.pending()) c.model.set({ evidenceReference: 'SYNTHETIC-GL-REVIEW' });
    TestBed.tick();
    return { f, c, http };
  }
  async function prepare(c: NativeLedgerSourcePage, http: HttpTestingController) {
    c.wholePeriod.set(true);
    TestBed.tick();
    const task = c.makePreview(),
      r = http.expectOne(root + '/preview'),
      body = r.request.body;
    r.flush({ value: preview });
    await task;
    TestBed.tick();
    return body;
  }
  it('keeps exact money, source IDs, nullable flags and sequence; rejects truncated or mismatched evidence', () => {
    const nativeTb = decodeNativeSource(tb, client);
    const decoded = decodeNativeLedgerPreview(preview, nativeTb);
    expect(decoded.rows[0].debit).toBe(exact);
    expect(decoded.postingSequence).toBe('9007199254740993');
    expect(decoded.rows[0].isManual).toBeNull();
    expect(decoded.rows[0].isYearEnd).toBeNull();
    for (const bad of [
      { ...preview, postingSequence: 9007199254740993 },
      { ...preview, totalDebit: 1 },
      { ...preview, trialBalanceDatasetId: actor },
      { ...preview, asOfDate: '2026-11-30' },
      { ...preview, originKind: 'EXTERNAL' },
      { ...preview, rows: rows.slice(0, 100) },
      { ...preview, rows: [rows[0], ...rows.slice(0, -1)] },
      { ...preview, rows: rows.map((r, i) => (i === 0 ? { ...r, isManual: 'false' } : r)) },
    ]) {
      expect(() => decodeNativeLedgerPreview(bad, nativeTb)).toThrow();
    }
    expect(() => decodeNativeLedgerSource({ ...source, clientId: actor }, client)).toThrow();
    expect(nativeLedgerPendingIntent({ reviewed: true })).toBeNull();
    const pending = {
      requestId: id(8),
      clientId: client,
      engagementId: engagement,
      trialBalanceDatasetId: dataset,
      expectedTrialBalanceSourceHash: tb.sourceIdentityHash,
      evidenceReference: 'E'.repeat(300),
      reviewBasis: preview.reviewBasis,
    };
    expect(nativeLedgerPendingIntent(pending)?.evidenceReference).toHaveLength(300);
    expect(
      nativeLedgerPendingIntent({ ...pending, evidenceReference: 'E'.repeat(301) }),
    ).toBeNull();
    expect(() =>
      decodeNativeLedgerArtifact(
        {
          ...source,
          fileName: '../ledger.json',
          contentType: 'application/json',
          generatedRowsJson: '[]',
          manifestJson: '{}',
        },
        decodeNativeLedgerSource(source, client),
      ),
    ).toThrow();
  });
  it('offers all actual preview rows before a fresh assent and one exact seal; never auto-accepts GL', async () => {
    const { f, c, http } = await open();
    await c.makePreview();
    http.expectNone(root + '/preview');
    c.wholePeriod.set(true);
    c.model.set({ evidenceReference: 'E'.repeat(301) });
    TestBed.tick();
    expect(c.ready()).toBe(false);
    await c.makePreview();
    http.expectNone(root + '/preview');
    c.model.set({ evidenceReference: 'E'.repeat(300) });
    TestBed.tick();
    expect(c.ready()).toBe(true);
    const body = await prepare(c, http);
    expect(body.evidenceReference).toHaveLength(300);
    f.detectChanges();
    expect(body).toMatchObject({
      clientId: client,
      engagementId: engagement,
      trialBalanceDatasetId: dataset,
      expectedTrialBalanceSourceHash: tb.sourceIdentityHash,
      reviewed: false,
      reviewBasis: null,
      periodLedgerReviewed: true,
    });
    expect(body.periodId).toBeUndefined();
    expect(body.bookId).toBeUndefined();
    expect(c.visibleRows()).toHaveLength(100);
    c.rowPage.set(1);
    f.detectChanges();
    expect(c.visibleRows()).toHaveLength(2);
    expect(c.visibleRows()[1].lineId).toBe(rows[101].lineId);
    expect(f.nativeElement.querySelector('mat-paginator')).toBeTruthy();
    expect(f.nativeElement.textContent).toContain('Unknown');
    expect(f.nativeElement.textContent).toContain('Unavailable');
    expect(f.nativeElement.textContent).toContain(exact);
    expect(f.nativeElement.textContent).toContain(facts.totalDebit);
    await c.seal();
    http.expectNone(root + '/seal');
    c.reviewed.set(true);
    const task = c.seal();
    await c.seal();
    const request = http.expectOne(root + '/seal');
    expect(request.request.body).toEqual({
      ...body,
      reviewBasis: preview.reviewBasis,
      reviewed: true,
    });
    request.flush({ value: source });
    await task;
    TestBed.tick();
    f.detectChanges();
    expect(c.displaySource()?.importBatchId).toBe(batch);
    expect(f.nativeElement.textContent).toContain('not an uploaded GL file');
    http.expectNone(`/api/ui/gl-sources/${batch}/acceptance`);
  });
  it('refuses seal on an immediate evidence edit and discards protected preview after session invalidation', async () => {
    const { c, http } = await open();
    await prepare(c, http);
    c.reviewed.set(true);
    c.model.set({ evidenceReference: 'CHANGED-UNREVIEWED' });
    await c.seal();
    http.expectNone(root + '/seal');
    TestBed.tick();
    expect(c.preview()).toBeNull();
    expect(c.reviewed()).toBe(false);
    c.wholePeriod.set(true);
    TestBed.tick();
    const pending = c.makePreview();
    const request = http.expectOne(root + '/preview');
    TestBed.inject(SessionService).invalidation.update((n) => n + 1);
    TestBed.inject(SessionService).current.set(null);
    TestBed.tick();
    request.flush({ value: preview });
    await pending;
    TestBed.tick();
    expect(c.preview()).toBeNull();
    expect(c.displaySource()).toBeNull();
    expect(c.canSeal()).toBe(false);
  });
  it('recovers an unknown original request across reload with no automatic retry or saved assent', async () => {
    let opened = await open();
    const body = await prepare(opened.c, opened.http);
    opened.c.reviewed.set(true);
    const send = opened.c.seal();
    opened.http.expectOne(root + '/seal').flush({}, { status: 503, statusText: 'Unavailable' });
    await send;
    expect(opened.c.uncertain()).toBe(true);
    expect(opened.c.reviewed()).toBe(false);
    opened.f.destroy();
    opened = await open();
    expect(opened.c.pending()?.requestId).toBe(body.requestId);
    await opened.c.seal();
    opened.http.expectNone(root + '/seal');
    opened.c.wholePeriod.set(true);
    TestBed.tick();
    opened.c.reviewed.set(true);
    const retry = opened.c.seal(),
      request = opened.http.expectOne(root + '/seal');
    expect(request.request.body).toEqual({
      ...body,
      reviewed: true,
      reviewBasis: preview.reviewBasis,
    });
    request.flush({ value: source });
    await retry;
    expect(opened.c.pending()).toBeNull();
    expect(opened.c.uncertain()).toBe(false);
  });
  it('reads a scoped retained historical source without enabling a fresh capture or automatic acceptance', async () => {
    params.next(convertToParamMap({ clientId: client, importBatchId: batch }));
    const f = TestBed.createComponent(NativeLedgerSourcePage);
    f.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectNone(tbUrl);
    http
      .expectOne(root + '/' + batch)
      .flush({ ...source, isCurrent: false, currentnessBlocker: 'Later native postings' });
    TestBed.tick();
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Historical source: Later native postings');
    expect(captureButton(f.nativeElement)).toBeNull();
    expect(f.nativeElement.textContent).toContain('Download exact generated GL rows');
    expect(f.nativeElement.textContent).not.toContain('Separate independent GL acceptance');
    await f.componentInstance.makePreview();
    http.expectNone(root + '/preview');
  });
});
function captureButton(element: HTMLElement) {
  return (
    Array.from(element.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Preview exact native GL rows'),
    ) ?? null
  );
}
