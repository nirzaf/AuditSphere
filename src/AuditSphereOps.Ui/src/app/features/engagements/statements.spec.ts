import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { StatementDrillDown } from './statements';
import {
  decodeStatementPage,
  decodeStatementContributions,
  statementLocation,
  sameStatementBasis,
} from './statement-contracts';
const id = '11111111-1111-4111-8111-111111111111',
  proc = '22222222-2222-4222-8222-222222222222';
const basis = {
  engagementId: id,
  clientId: id,
  mappingId: id,
  mappingVersion: 1,
  datasetId: id,
  datasetRevision: 1,
  datasetDigest: 'a'.repeat(64),
  currency: 'QAR',
  periodStart: '2026-01-01',
  periodEnd: '2026-12-31',
  taxonomyVersion: 'syn-v1',
  chartVersionId: id,
  generation: 1,
  reviewerId: id,
  reviewedAt: '2026-10-02T00:00:00Z',
  revision: 'b'.repeat(64),
};
const summary = {
  basis,
  section: 'position',
  title: 'Statement of financial position',
  totalLabel: 'Assets less claims',
  total: '100.123456',
  balances: true,
  lineCount: 1,
  filteredCount: 1,
  page: 1,
  pageSize: 25,
  lines: [
    {
      destinationCode: 'CASH',
      statementSection: 'ASSETS',
      auditArea: 'Cash',
      amount: '100.123456',
      accountCount: 1,
      procedureCount: 1,
    },
  ],
};
const detail = {
  basis,
  section: 'position',
  destinationCode: 'CASH',
  statementSection: 'ASSETS',
  lineTotal: '100.123456',
  accountCount: 1,
  page: 1,
  pageSize: 25,
  accounts: [{ accountCode: '1000', accountName: 'Synthetic cash', amount: '100.123456' }],
  procedureCount: 1,
  procedurePage: 1,
  procedures: [
    {
      procedureId: proc,
      sourceProcedureId: 'CSH-01',
      title: 'Synthetic verification',
      status: 'PLANNED',
      section: 'Cash',
    },
  ],
};
describe('Statement review contracts and navigation', () => {
  it('keeps exact decimals and refuses overlarge or numeric pages', () => {
    expect(decodeStatementPage(summary, '').total).toBe('100.123456');
    expect(() => decodeStatementPage({ ...summary, total: 100.123456 }, '')).toThrow();
    expect(() => decodeStatementPage({ ...summary, pageSize: 100 }, '')).toThrow();
    expect(() =>
      decodeStatementContributions({ ...detail, accounts: Array(26).fill(detail.accounts[0]) }, ''),
    ).toThrow();
  });
  it('refuses malformed URL metadata without making a source request', () => {
    for (const bad of [
      { section: 'unknown' },
      { page: '0' },
      { page: '1001' },
      { filter: 'x'.repeat(81) },
      { line: 'CASH' },
      { procedure: '-'.repeat(36) },
      { basis: 'fake' },
    ]) {
      expect(
        statementLocation((k) => (bad as Record<string, string | undefined>)[k] ?? null),
      ).toBeNull();
    }
    expect(
      statementLocation(
        (k) =>
          (
            ({
              section: 'position',
              filter: 'cash',
              page: '2',
              line: 'CASH',
              lineSection: 'ASSETS',
            }) as Record<string, string>
          )[k] ?? null,
      )?.page,
    ).toBe(2);
  });
  it('refuses mixed dataset, generation, currency and line context', () => {
    const s = decodeStatementPage(summary, ''),
      d = decodeStatementContributions(detail, '');
    expect(sameStatementBasis(s, d, 'CASH', 'ASSETS')).toBe(true);
    for (const b of [
      { ...basis, datasetId: proc },
      { ...basis, currency: 'USD' },
      { ...basis, generation: 2 },
    ])
      expect(sameStatementBasis(s, { ...d, basis: b }, 'CASH', 'ASSETS')).toBe(false);
    expect(sameStatementBasis(s, d, 'OTHER', 'ASSETS')).toBe(false);
  });
});
describe('Statement review protected async state', () => {
  let query: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    query = new BehaviorSubject(
      convertToParamMap({
        section: 'position',
        line: 'CASH',
        lineSection: 'ASSETS',
        basis: basis.revision,
      }),
    );
    TestBed.configureTestingModule({
      imports: [StatementDrillDown],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: new BehaviorSubject(convertToParamMap({ id })),
            queryParamMap: query,
            snapshot: { queryParamMap: query.value },
          },
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
  function open() {
    const f = TestBed.createComponent(StatementDrillDown);
    f.detectChanges();
    TestBed.tick();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((r) => r.url.includes('/statements/workspace')).flush(summary);
    TestBed.tick();
    http.expectOne((r) => r.url.includes('/statements/contributions')).flush(detail);
    f.detectChanges();
    TestBed.tick();
    return { f, http, c: f.componentInstance };
  }
  it('shows complete totals and exact accounts then clears them before refresh', () => {
    const { f, http, c } = open();
    expect(f.nativeElement.textContent).toContain('100.123456');
    expect(c.currentDetail()?.accounts[0].accountCode).toBe('1000');
    c.refresh();
    expect(c.view.data()).toBeNull();
    TestBed.tick();
    expect(c.currentDetail()).toBeNull();
    http
      .expectOne((r) => r.url.includes('/workspace'))
      .flush({ message: 'Review a new mapping.' }, { status: 409, statusText: 'Conflict' });
    f.detectChanges();
    expect(f.nativeElement.textContent).not.toContain('100.123456');
    expect(c.view.error()).toContain('Review a new mapping');
  });
  it('restores bounded row metadata but hides changed-basis evidence until explicit acknowledgment', () => {
    const { f, http, c } = open();
    c.refresh();
    TestBed.tick();
    http
      .expectOne((r) => r.url.includes('/workspace'))
      .flush({ ...summary, basis: { ...basis, revision: 'c'.repeat(64) } });
    TestBed.tick();
    f.detectChanges();
    expect(c.stale()).toBe(true);
    expect(c.currentDetail()).toBeNull();
    expect(f.nativeElement.textContent).toContain('Statement basis changed');
    http.expectNone((r) => r.url.includes('/contributions'));
    expect(c.location()?.line).toBe('CASH');
  });
  it('requests a procedure only from the verified line and clears all evidence on revocation', () => {
    const { f, http, c } = open();
    query.next(
      convertToParamMap({
        section: 'position',
        line: 'CASH',
        lineSection: 'ASSETS',
        basis: basis.revision,
        procedure: proc,
      }),
    );
    TestBed.tick();
    http
      .expectOne('/api/ui/procedures/' + proc + '/review')
      .flush({ procedureId: proc, currentResult: null, evidence: [] });
    TestBed.tick();
    f.detectChanges();
    expect(c.currentEvidence()?.procedureId).toBe(proc);
    expect(f.nativeElement.textContent).toContain('No linked received files');
    TestBed.inject(SessionService).clear();
    TestBed.tick();
    f.detectChanges();
    expect(c.currentEvidence()).toBeNull();
    expect(c.currentDetail()).toBeNull();
    expect(c.view.data()).toBeNull();
    expect(f.nativeElement.textContent).not.toContain('100.123456');
  });
  it('reads the current approved basis when returning from supporting evidence', () => {
    const { f, http, c } = open();
    query.next(
      convertToParamMap({
        section: 'position',
        line: 'CASH',
        lineSection: 'ASSETS',
        basis: basis.revision,
        procedure: proc,
      }),
    );
    TestBed.tick();
    http
      .expectOne('/api/ui/procedures/' + proc + '/review')
      .flush({ procedureId: proc, currentResult: null, evidence: [] });
    TestBed.tick();
    query.next(
      convertToParamMap({
        section: 'position',
        line: 'CASH',
        lineSection: 'ASSETS',
        basis: basis.revision,
      }),
    );
    TestBed.tick();
    expect(c.currentEvidence()).toBeNull();
    expect(c.currentDetail()).toBeNull();
    http
      .expectOne((r) => r.url.includes('/workspace'))
      .flush({ ...summary, basis: { ...basis, revision: 'c'.repeat(64) } });
    TestBed.tick();
    f.detectChanges();
    expect(c.stale()).toBe(true);
    expect(c.location()?.line).toBe('CASH');
    http.expectNone((r) => r.url.includes('/contributions'));
  });
  it('does not load an unrelated supporting identity from the URL', () => {
    const { f, http, c } = open();
    query.next(
      convertToParamMap({
        section: 'position',
        line: 'CASH',
        lineSection: 'ASSETS',
        basis: basis.revision,
        procedure: id,
      }),
    );
    TestBed.tick();
    http.expectNone('/api/ui/procedures/' + id + '/review');
    expect(c.currentEvidence()).toBeNull();
    f.destroy();
  });
  it('rejects a wrong engagement response instead of rendering its totals', () => {
    const f = TestBed.createComponent(StatementDrillDown);
    f.detectChanges();
    TestBed.tick();
    TestBed.inject(HttpTestingController)
      .expectOne((r) => r.url.includes('/workspace'))
      .flush({ ...summary, basis: { ...basis, engagementId: proc } });
    TestBed.tick();
    f.detectChanges();
    expect(f.componentInstance.view.data()).toBeNull();
    expect(f.nativeElement.textContent).not.toContain('100.123456');
  });
});
