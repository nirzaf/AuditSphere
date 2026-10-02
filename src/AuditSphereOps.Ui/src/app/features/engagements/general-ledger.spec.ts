import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { GeneralLedger } from './general-ledger';
import {
  decodeLedgerCatalogue,
  decodeLedgerJournal,
  decodeLedgerSource,
} from './general-ledger-contracts';
import { SessionService } from '../../core/session';
const id = '11111111-1111-4111-8111-111111111111',
  batch = '22222222-2222-4222-8222-222222222222',
  other = '33333333-3333-4333-8333-333333333333';
const context = {
  batchId: batch,
  clientId: id,
  engagementId: id,
  periodId: id,
  periodCode: '2026',
  bookId: null,
  entity: 'Synthetic ledger',
  currency: 'QAR',
  importState: 'SEALED',
  rawFileSha256: 'a'.repeat(64),
  sourceHash: 'b'.repeat(64),
  profileVersion: 'syn-v1',
  parserVersion: 'syn-v1',
  importedByUserId: id,
  importedAt: '2026-10-01T00:00:00Z',
  selected: null,
};
const catalogue = {
  clientId: id,
  engagementId: id,
  items: [
    {
      id: batch,
      periodId: id,
      periodCode: '2026',
      bookId: null,
      entity: 'Synthetic ledger',
      currency: 'QAR',
      rowCount: 101,
      sourceHash: 'b'.repeat(64),
      importedAt: '2026-10-01T00:00:00Z',
    },
  ],
  totalCount: 1,
  page: 1,
  pageSize: 20,
};
const line = {
  lineId: id,
  stableJournalId: 'J-001',
  stableLineId: 'L-001',
  postingDate: '2026-06-30',
  accountCode: '1000',
  debit: '100.123456',
  credit: '0',
  functionalAmount: '100.123456',
  originalCurrency: 'QAR',
  originalAmount: '100.123456',
  counterparty: '',
  branch: '',
  costCentre: '',
  department: '',
  project: '',
};
const filter = {
  accountCodePrefix: '',
  postedFrom: null,
  postedTo: null,
  stableJournalId: '',
  counterparty: '',
};
const source = {
  context,
  filter,
  rows: {
    items: [line],
    totalCount: 101,
    page: 1,
    pageSize: 100,
    totalDebit: '100.123456',
    totalCredit: '0',
    balanced: false,
  },
  journalLineLimit: 1000,
};
const journal = {
  context,
  journalLineLimit: 1000,
  journal: {
    stableJournalId: 'J-001',
    postingDate: '2026-06-30',
    documentNumber: 'DOC-001',
    currency: 'QAR',
    isManual: false,
    isYearEnd: false,
    reversalReference: null,
    lines: [line],
    lineCount: 1,
    totalDebit: '100.123456',
    totalCredit: '0',
    balanced: false,
  },
};
const base = `/api/ui/engagements/${id}/general-ledger`;
describe('Native bounded general ledger inspection', () => {
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    params = new BehaviorSubject(convertToParamMap({ id }));
    TestBed.configureTestingModule({
      imports: [GeneralLedger],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
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
  function open() {
    const f = TestBed.createComponent(GeneralLedger);
    f.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(base + '?page=1').flush(catalogue);
    f.detectChanges();
    const c = f.componentInstance;
    c.choose(batch);
    TestBed.tick();
    http.expectOne((r) => r.url.startsWith(base + '/' + batch + '?')).flush(source);
    f.detectChanges();
    return { f, c, http };
  }
  it('preserves exact source amounts and server page identity; refresh clears prior rows', () => {
    const { f, c, http } = open();
    expect(f.nativeElement.textContent).toContain('100.123456');
    expect(f.nativeElement.textContent).toContain('No GL source has been independently accepted');
    c.changeRows(1);
    TestBed.tick();
    expect(c.source.data()).toBeNull();
    http
      .expectOne((r) => r.url.includes('page=2'))
      .flush({ ...source, rows: { ...source.rows, page: 2 } });
    c.refreshSource();
    expect(c.source.data()).toBeNull();
    http
      .expectOne((r) => r.url.includes('page=2'))
      .flush({ message: 'Denied' }, { status: 403, statusText: 'Forbidden' });
    expect(c.source.data()).toBeNull();
    f.destroy();
  });
  it('binds server filters and journal reads; edited filters hide obsolete results', () => {
    const { f, c, http } = open();
    c.journalId.set('J-001');
    TestBed.tick();
    http.expectOne((r) => r.url.includes('/journal?')).flush(journal);
    f.detectChanges();
    expect(c.journal.data()?.journal.lineCount).toBe(1);
    c.model.set({ ...c.model(), account: '10' });
    TestBed.tick();
    f.detectChanges();
    expect(c.stale()).toBe(true);
    expect(c.journal.data()).toBeNull();
    expect(f.nativeElement.textContent).not.toContain(
      'Exact amounts for the filtered ledger population',
    );
    c.apply();
    TestBed.tick();
    http
      .expectOne((r) => r.url.includes('account=10'))
      .flush({ ...source, filter: { ...filter, accountCodePrefix: '10' } });
    expect(c.stale()).toBe(false);
    c.journalId.set('J-001');
    TestBed.tick();
    http
      .expectOne((r) => r.url.includes('/journal?'))
      .flush({ ...journal, context: { ...context, batchId: other } });
    expect(c.journal.data()).toBeNull();
    expect(c.journal.error()).toContain('Unsupported');
    c.model.set({ ...c.model(), from: '2026-10-01', to: '2026-01-01' });
    expect(c.validFilters()).toBe(false);
    f.destroy();
  });
  it('clears protected selections and cancels reads after route or session changes', () => {
    const { f, c, http } = open();
    c.journalId.set('J-001');
    TestBed.tick();
    const pending = http.expectOne((r) => r.url.includes('/journal?'));
    params.next(convertToParamMap({ id: other }));
    TestBed.tick();
    expect(pending.cancelled).toBe(true);
    expect(c.batchId()).toBeNull();
    expect(c.source.data()).toBeNull();
    expect(c.journal.data()).toBeNull();
    http
      .expectOne(`/api/ui/engagements/${other}/general-ledger?page=1`)
      .flush({ ...catalogue, engagementId: other });
    c.choose(other);
    expect(c.batchId()).toBeNull();
    TestBed.inject(SessionService).invalidation.update((x) => x + 1);
    TestBed.inject(SessionService).current.set(null);
    TestBed.tick();
    expect(c.catalogue.data()).toBeNull();
    f.destroy();
  });
  it('rejects unsupported page sizes, missing identities, truncated or duplicated journal evidence', () => {
    expect(() => decodeLedgerCatalogue({ ...catalogue, pageSize: 500 })).toThrow();
    expect(() =>
      decodeLedgerSource({ ...source, context: { ...context, sourceHash: '' } }),
    ).toThrow();
    expect(() =>
      decodeLedgerSource({ ...source, rows: { ...source.rows, items: [line, line] } }),
    ).toThrow();
    expect(() =>
      decodeLedgerJournal({ ...journal, journal: { ...journal.journal, lineCount: 1001 } }),
    ).toThrow();
    expect(() =>
      decodeLedgerSource({
        ...source,
        context: {
          ...context,
          selected: {
            decisionId: id,
            sourceKind: 'TB',
            trialBalanceDatasetId: id,
            importBatchId: null,
            sourceIdentityHash: 'b'.repeat(64),
            acceptedByUserId: id,
            acceptedAt: context.importedAt,
            inputGeneration: 1,
          },
        },
      }),
    ).toThrow();
  });
});
