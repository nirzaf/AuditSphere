import { provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TrialBalanceSource, decodeSource } from './tb-source';
import { SessionService } from '../../core/session';
const id = '11111111-1111-4111-8111-111111111111',
  other = '22222222-2222-4222-8222-222222222222';
const row = {
  id,
  datasetId: id,
  accountCode: '1000',
  accountName: 'Synthetic cash',
  amount: '100.123456',
  sourceDebit: null,
  sourceCredit: null,
  currency: 'QAR',
  entity: 'SYN',
  mappingCode: null,
};
const response = {
  datasetId: id,
  clientId: id,
  engagementId: id,
  periodId: null,
  periodCode: null,
  revision: 1,
  currency: 'QAR',
  entity: 'SYN',
  sourceKind: 'Raw',
  importState: 'SEALED',
  validationStatus: 'Pending',
  balanced: true,
  rawFileSha256: 'a'.repeat(64),
  normalizedDigest: 'b'.repeat(64),
  accountCodeFilter: '',
  rows: {
    items: [row],
    totalCount: 101,
    page: 1,
    pageSize: 100,
    totalAmount: '0.000000',
    totalDebit: null,
    totalCredit: null,
  },
  issues: { items: [], totalCount: 0, page: 1, pageSize: 100 },
  exportRowLimit: 50000,
  exportByteLimit: 25 * 1024 * 1024,
};
const url = `/api/ui/datasets/${id}/source?filter=&page=1&issuePage=1`;
describe('Native bounded source inspection', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TrialBalanceSource],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
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
    const f = TestBed.createComponent(TrialBalanceSource);
    f.componentRef.setInput('datasetId', id);
    f.componentRef.setInput('clientId', id);
    f.componentRef.setInput('engagementId', id);
    f.detectChanges();
    TestBed.tick();
    return { f, c: f.componentInstance, http: TestBed.inject(HttpTestingController) };
  }
  it('retains exact amounts, marks pending acceptance and requests only the next server page', () => {
    const { f, c, http } = open();
    http.expectOne(url).flush(response);
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('100.123456');
    expect(f.nativeElement.textContent).toContain('not eligible for mapping');
    c.rowPage.set(2);
    TestBed.tick();
    expect(c.source.data()).toBeNull();
    http
      .expectOne(url.replace('page=1', 'page=2'))
      .flush({ ...response, rows: { ...response.rows, page: 2 } });
    expect(c.source.data()?.rows.page).toBe(2);
    f.destroy();
  });
  it('withdraws source rows on a failed refresh and refuses another scope', () => {
    const { f, c, http } = open();
    http.expectOne(url).flush(response);
    c.refresh();
    expect(c.source.data()).toBeNull();
    http.expectOne(url).flush({ message: 'Unavailable' }, { status: 403, statusText: 'Forbidden' });
    expect(c.source.data()).toBeNull();
    c.refresh();
    http.expectOne(url).flush({ ...response, engagementId: other });
    expect(c.source.data()).toBeNull();
    expect(c.source.error()).toContain('Unsupported');
    f.destroy();
  });
  it('requires applied filters before export and clears results on context/session changes', () => {
    const { f, c, http } = open();
    http.expectOne(url).flush(response);
    c.model.set({ filter: '10' });
    expect(c.stale()).toBe(true);
    c.applyFilter();
    TestBed.tick();
    http
      .expectOne(url.replace('filter=', 'filter=10'))
      .flush({ ...response, accountCodeFilter: '10' });
    f.componentRef.setInput('datasetId', other);
    TestBed.tick();
    expect(c.model().filter).toBe('');
    http.expectOne(url.replace(id, other)).flush({
      ...response,
      datasetId: other,
      rows: { ...response.rows, items: [{ ...row, datasetId: other }] },
    });
    TestBed.inject(SessionService).clear();
    TestBed.tick();
    expect(c.source.data()).toBeNull();
    f.destroy();
  });
  it('fails closed on rounded money, unsealed sources, oversized windows and another row identity', () => {
    for (const v of [
      { ...response, importState: 'LOADING' },
      { ...response, rows: { ...response.rows, items: [{ ...row, amount: 100.123456 }] } },
      { ...response, rows: { ...response.rows, items: [{ ...row, datasetId: other }] } },
      { ...response, rows: { ...response.rows, items: Array.from({ length: 101 }, () => row) } },
    ])
      expect(() => decodeSource(v)).toThrow();
  });
});
