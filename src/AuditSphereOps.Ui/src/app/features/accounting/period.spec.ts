import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { AccountingPeriodRecord, decodePeriodRecord } from './period';
import { decodePeriodEvidencePage, PERIOD_EVIDENCE_KINDS } from './period-evidence';
import type { PeriodEvidenceKind } from './period-evidence';

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const record = (n = 1) => ({
  id: id(n),
  clientId: id(10),
  clientName: 'Synthetic period client',
  engagement: 'Audit',
  periodCode: 'FY2026',
  startDate: '2026-01-01',
  endDate: '2026-12-31',
  basis: 'STATUTORY',
  book: 'MAIN',
  currency: 'QAR',
  status: 'CLOSED',
  revision: '2',
  priorPeriodId: null,
  packageId: id(20),
  packageLabel: 'VALIDATED',
  mappingId: id(21),
  mappingLabel: 'APPROVED',
  pbc: { engagementId: id(30), owner: 'Synthetic liaison', dueDate: '2026-12-31', state: 'OPEN' },
  task: { title: 'Synthetic task', owner: 'Synthetic staff', dueDate: '2026-12-30', state: 'OPEN' },
});

describe('exact accounting period record and handoffs', () => {
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let http: HttpTestingController;
  beforeEach(() => {
    params = new BehaviorSubject(convertToParamMap({ id: id(1) }));
    TestBed.configureTestingModule({
      imports: [AccountingPeriodRecord],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({
      userId: id(90),
      firmId: id(91),
      generation: '1',
      staff: true,
    });
  });
  afterEach(() => {
    http.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });
  function open() {
    const fixture = TestBed.createComponent(AccountingPeriodRecord);
    fixture.detectChanges();
    TestBed.tick();
    return fixture;
  }
  function readPeriod(value: object, n = 1) {
    http.expectOne('/api/ui/accounting/periods/' + id(n)).flush(value);
    TestBed.tick();
  }
  function readEvidence(n = 1) {
    const index = http.expectOne('/api/ui/accounting/periods/' + id(n) + '/evidence');
    expect(index.request.method).toBe('GET');
    index.flush(evidenceIndex(id(n), id(10), 27));
    TestBed.tick();
    const page = http.expectOne('/api/ui/accounting/periods/' + id(n) + '/evidence/books?page=1');
    expect(page.request.method).toBe('GET');
    page.flush(evidencePage(id(n), id(10), 'books', 1, 27, 25));
    TestBed.tick();
  }
  function read(value: object, n = 1) {
    readPeriod(value, n);
    readEvidence(n);
  }

  it('preserves large period revisions as invariant Int64 strings', () => {
    const revision = '9007199254740993';
    const decoded = decodePeriodRecord({ ...record(), revision }, 'response');
    expect(decoded.revision).toBe(revision);
    expect(() => decodePeriodRecord({ ...record(), revision: 9007199254740992 }, 'response')).toThrow();
    expect(() => decodePeriodRecord({ ...record(), revision: '09007199254740993' }, 'response')).toThrow();
  });

  function evidenceIndex(periodId: string, clientId: string, bookCount: number,
    counts: Partial<Record<PeriodEvidenceKind, number>> = {}) {
    return {
      periodId,
      clientId,
      categories: PERIOD_EVIDENCE_KINDS.map(kind => ({
        kind,
        totalCount: counts[kind] ?? (kind === 'books' ? bookCount : 0),
        pageSize: 25,
      })),
      unrepresentedCategories: ['bank-schedules', 'receivables-schedules', 'payables-schedules', 'monthly-handover'],
    };
  }

  function trialBalanceEvidence() {
    return {
      id: id(200), engagementId: id(30), bookId: null, basis: null, sourceKind: 'Raw',
      revision: '9223372036854775807', legalEntityKey: 'ENTITY-1', currency: 'QAR',
      rawFileSha256Hex: '', normalizedDatasetDigest: '', sha256Hex: '', balanced: false,
      validationStatus: 'Accepted', importState: 'SEALED', controlTotal: '0.000000',
      latestAcceptanceReceiptId: id(300), importedAt: '2026-10-20T12:00:00Z', importedByUserId: id(90),
    };
  }

  function evidencePage(periodId: string, clientId: string, kind: PeriodEvidenceKind, page: number, totalCount: number, rowCount: number) {
    const offset = (page - 1) * 25;
    const items = kind === 'books'
      ? Array.from({ length: rowCount }, (_, index) => {
          const position = offset + index;
          return {
            id: id(2000 + position),
            code: position === 0 ? 'MAIN' : `SYN-EVIDENCE-${String(position - 1).padStart(2, '0')}`,
            basis: 'STATUTORY', inclusionRule: 'ALL_ENTITIES', currency: 'QAR', status: 'ACTIVE',
            revision: '9223372036854775807',
          };
        })
      : kind === 'trial-balances' && rowCount > 0 ? [trialBalanceEvidence()] : [];
    return { periodId, clientId, kind, page, pageSize: 25, totalCount, items };
  }

  it('renders recorded owners and exact package, mapping and PBC links without commands', () => {
    const fixture = open();
    read(record());
    const root = fixture.nativeElement as HTMLElement;
    const links = Array.from(root.querySelectorAll('a')).map((a) => a.getAttribute('href'));
    expect(links).toContain('/app/accounting/packages/' + id(20));
    expect(links).toContain('/app/accounting/mappings/' + id(21));
    expect(links).toContain('/app/engagements/' + id(30) + '/pbc');
    expect(root.textContent).toContain('Synthetic liaison');
    expect(root.textContent).toContain('Synthetic staff');
    expect(root.textContent).toContain('Period evidence');
    const categoryLabel = root.querySelector('label[for="period-evidence-category"]');
    expect(categoryLabel?.textContent?.trim()).toBe('Evidence category');
    http.expectNone((r) => r.method !== 'GET');
  });

  it('shows explicit not-assessed coverage and pages the confirmed category wrapper with accessible controls', () => {
    const fixture = open();
    read(record());
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Not assessed in this view');
    expect(root.textContent).toContain('Bank schedules');
    expect(root.textContent).toContain('Receivables schedules');
    expect(root.textContent).toContain('Payables schedules');
    expect(root.textContent).toContain('Monthly handover');
    expect((root.querySelector('#period-evidence-category') as HTMLSelectElement).value).toBe('books');
    expect(root.textContent).toContain('27 records');
    expect(root.querySelector('[aria-label="Period evidence table scroll area"]')).not.toBeNull();

    const next = Array.from(root.querySelectorAll('button')).find(button => button.textContent?.includes('Next evidence page'))!;
    next.click();
    TestBed.tick();
    const pageTwo = http.expectOne('/api/ui/accounting/periods/' + id(1) + '/evidence/books?page=2');
    expect(pageTwo.request.method).toBe('GET');
    pageTwo.flush(evidencePage(id(1), id(10), 'books', 2, 27, 2));
    TestBed.tick();
    expect(root.textContent).toContain('Page 2 of 2');
    expect(root.textContent).toContain('SYN-EVIDENCE-25');
    expect(root.textContent).toContain('Previous evidence page');
    expect(root.textContent).toContain('Next evidence page');

    const selector = root.querySelector('#period-evidence-category') as HTMLSelectElement;
    selector.value = 'trial-balances';
    selector.dispatchEvent(new Event('change'));
    TestBed.tick();
    const firstTrialBalancePage = http.expectOne('/api/ui/accounting/periods/' + id(1) + '/evidence/trial-balances?page=1');
    firstTrialBalancePage.flush(evidencePage(id(1), id(10), 'trial-balances', 1, 0, 0));
    TestBed.tick();
    expect(root.textContent).toContain('Trial balances · 0 records · Page 1 of 1');
    http.expectNone(request => request.method !== 'GET');
  });

  it('rejects an evidence index with a mismatched client identity before requesting a category page', () => {
    const fixture = open();
    http.expectOne('/api/ui/accounting/periods/' + id(1)).flush(record());
    TestBed.tick();
    http.expectOne('/api/ui/accounting/periods/' + id(1) + '/evidence')
      .flush(evidenceIndex(id(1), id(11), 27));
    TestBed.tick();
    expect(fixture.nativeElement.textContent).not.toContain('27 records');
    expect(fixture.componentInstance.period.data()?.id).toBe(id(1));
    expect(fixture.nativeElement.textContent).toContain('Unsupported response');
    http.expectNone(request => request.url.endsWith('/evidence/books?page=1'));
  });

  it('rejects category pages with a mismatched exact client identity and clears their rows', () => {
    const fixture = open();
    http.expectOne('/api/ui/accounting/periods/' + id(1)).flush(record());
    TestBed.tick();
    http.expectOne('/api/ui/accounting/periods/' + id(1) + '/evidence')
      .flush(evidenceIndex(id(1), id(10), 27));
    TestBed.tick();
    http.expectOne('/api/ui/accounting/periods/' + id(1) + '/evidence/books?page=1')
      .flush(evidencePage(id(1), id(11), 'books', 1, 27, 1));
    TestBed.tick();
    expect(fixture.nativeElement.textContent).not.toContain('retained record(s)');
    expect(fixture.nativeElement.textContent).toContain('Unsupported response');
  });

  it('preserves Int64 sequence strings and rejects numeric or non-invariant revisions', () => {
    const page = evidencePage(id(1), id(10), 'books', 1, 1, 1);
    const decoded = decodePeriodEvidencePage(page, 'response');
    expect(decoded.kind).toBe('books');
    if (decoded.kind === 'books') expect(decoded.items[0].revision).toBe('9223372036854775807');

    const numericRevision = {
      ...page,
      items: [{ ...page.items[0], revision: 9223372036854775807 }],
    };
    expect(() => decodePeriodEvidencePage(numericRevision, 'response')).toThrow();
    const nonInvariantRevision = {
      ...page,
      items: [{ ...page.items[0], revision: '09223372036854775807' }],
    };
    expect(() => decodePeriodEvidencePage(nonInvariantRevision, 'response')).toThrow();
  });

  it('labels TB worker validation and retained receipts without rejecting blank legacy hashes', () => {
    const fixture = open();
    http.expectOne('/api/ui/accounting/periods/' + id(1)).flush(record());
    TestBed.tick();
    http.expectOne('/api/ui/accounting/periods/' + id(1) + '/evidence')
      .flush(evidenceIndex(id(1), id(10), 0, { 'trial-balances': 1 }));
    TestBed.tick();
    http.expectOne('/api/ui/accounting/periods/' + id(1) + '/evidence/books?page=1')
      .flush(evidencePage(id(1), id(10), 'books', 1, 0, 0));
    TestBed.tick();

    const root = fixture.nativeElement as HTMLElement;
    const selector = root.querySelector('#period-evidence-category') as HTMLSelectElement;
    selector.value = 'trial-balances';
    selector.dispatchEvent(new Event('change'));
    TestBed.tick();
    http.expectOne('/api/ui/accounting/periods/' + id(1) + '/evidence/trial-balances?page=1')
      .flush(evidencePage(id(1), id(10), 'trial-balances', 1, 1, 1));
    TestBed.tick();

    expect(root.textContent).toContain('Import validation');
    expect(root.textContent).toContain('Accepted');
    expect(root.textContent).toContain('Raw file SHA-256');
    expect(root.textContent).toContain('Not recorded');
    expect(root.textContent).toContain('Retained acceptance receipt ID');
    expect(root.textContent).toContain('does not identify the currently selected source');
    expect(root.querySelector(`a[href="/app/accounting/sources/${id(200)}/acceptance"]`)).not.toBeNull();
  });

  it.each(['both', 'task', 'pbc'] as const)('keeps missing %s handoffs honest', (missing) => {
    const fixture = open();
    read({
      ...record(),
      packageId: null,
      packageLabel: null,
      mappingId: null,
      mappingLabel: null,
      task: missing === 'both' || missing === 'task' ? null : record().task,
      pbc: missing === 'both' || missing === 'pbc' ? null : record().pbc,
    });
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Not recorded');
    expect(root.textContent?.includes('No open PBC request')).toBe(missing === 'both');
    expect(root.querySelector('a[href*="/packages/"]')).toBeNull();
    expect(root.querySelector('a[href*="/mappings/"]')).toBeNull();
  });

  it('rejects a validly shaped response for a different period without displaying it', () => {
    const fixture = open();
    readPeriod({ ...record(2), clientName: 'Do not display wrong identity' });
    expect(fixture.componentInstance.period.data()).toBeNull();
    expect(fixture.componentInstance.period.error()).not.toBe('');
    expect(fixture.nativeElement.textContent).not.toContain('Do not display wrong identity');
    http.expectNone(request => request.url.includes('/evidence'));
  });

  it('cancels earlier A to B to A requests and retains only the newest record', () => {
    const fixture = open();
    const first = http.expectOne('/api/ui/accounting/periods/' + id(1));
    params.next(convertToParamMap({ id: id(2) }));
    TestBed.tick();
    const second = http.expectOne('/api/ui/accounting/periods/' + id(2));
    params.next(convertToParamMap({ id: id(1) }));
    TestBed.tick();
    expect(first.cancelled).toBe(true);
    expect(second.cancelled).toBe(true);
    expect(fixture.componentInstance.period.data()).toBeNull();
    read({ ...record(), revision: '3' });
    expect(fixture.componentInstance.period.data()?.revision).toBe('3');
  });

  it('resets evidence to page one and clears prior period rows on a period route change', () => {
    const fixture = open();
    read(record());
    const root = fixture.nativeElement as HTMLElement;
    const next = Array.from(root.querySelectorAll('button')).find(button => button.textContent?.includes('Next evidence page'))!;
    next.click();
    TestBed.tick();
    http.expectOne('/api/ui/accounting/periods/' + id(1) + '/evidence/books?page=2')
      .flush(evidencePage(id(1), id(10), 'books', 2, 27, 2));
    TestBed.tick();

    params.next(convertToParamMap({ id: id(2) }));
    TestBed.tick();
    expect(fixture.componentInstance.period.data()).toBeNull();
    expect(root.textContent).not.toContain('27 records');
    http.expectOne('/api/ui/accounting/periods/' + id(2)).flush(record(2));
    TestBed.tick();
    http.expectOne('/api/ui/accounting/periods/' + id(2) + '/evidence')
      .flush(evidenceIndex(id(2), id(10), 27));
    TestBed.tick();
    http.expectOne('/api/ui/accounting/periods/' + id(2) + '/evidence/books?page=1')
      .flush(evidencePage(id(2), id(10), 'books', 1, 27, 25));
    TestBed.tick();
    expect(root.textContent).toContain('Page 1 of 2');
    expect(root.textContent).toContain('Books · 27');
  });

  it('clears prior details immediately on refresh and preserves denial without stale links', () => {
    const fixture = open();
    read(record());
    fixture.componentInstance.period.reload();
    TestBed.tick();
    expect(fixture.componentInstance.period.data()).toBeNull();
    http
      .expectOne('/api/ui/accounting/periods/' + id(1))
      .flush({ message: 'Access denied.' }, { status: 403, statusText: 'Forbidden' });
    TestBed.tick();
    expect(fixture.nativeElement.textContent).not.toContain('Synthetic period client');
    expect(fixture.nativeElement.querySelector('a[href*="/packages/"]')).toBeNull();
    expect(fixture.componentInstance.period.error()).not.toBe('');
  });

  it('clears protected details and links after a 503 refresh and restores them only after retry succeeds', () => {
    const fixture = open();
    read(record());
    const root = fixture.nativeElement as HTMLElement;
    const refresh = () => {
      const button = Array.from(root.querySelectorAll('button')).find(b => b.textContent?.includes('Refresh period'))!;
      button.click();
      TestBed.tick();
    };
    refresh();
    expect(fixture.componentInstance.period.data()).toBeNull();
    expect(root.textContent).not.toContain('Synthetic period client');
    http.expectOne('/api/ui/accounting/periods/' + id(1))
      .flush({}, { status: 503, statusText: 'Service Unavailable' });
    TestBed.tick();
    expect(fixture.componentInstance.period.error()).toBe('Temporarily unavailable. Retry shortly.');
    expect(root.textContent).not.toContain('Synthetic liaison');
    expect(root.textContent).not.toContain('Synthetic staff');
    for (const path of ['/packages/', '/mappings/', '/pbc']) {
      expect(root.querySelector(`a[href*="${path}"]`)).toBeNull();
    }
    refresh();
    expect(fixture.componentInstance.period.data()).toBeNull();
    read({ ...record(), revision: '3' });
    expect(fixture.componentInstance.period.error()).toBe('');
    expect(fixture.componentInstance.period.data()?.revision).toBe('3');
    expect(root.textContent).toContain('Synthetic period client');
    expect(root.textContent).toContain('Synthetic liaison');
    expect(root.querySelector(`a[href="/app/accounting/packages/${id(20)}"]`)).not.toBeNull();
    expect(root.querySelector(`a[href="/app/accounting/mappings/${id(21)}"]`)).not.toBeNull();
    expect(root.querySelector(`a[href="/app/engagements/${id(30)}/pbc"]`)).not.toBeNull();
    http.expectNone(r => r.method !== 'GET');
  });

  it('clears protected data on session loss and makes no read for an invalid route', () => {
    const fixture = open();
    read(record());
    TestBed.inject(SessionService).clear();
    TestBed.tick();
    expect(fixture.componentInstance.period.data()).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Synthetic period client');
    params.next(convertToParamMap({ id: 'invalid' }));
    TestBed.tick();
    http.expectNone((r) => r.url.startsWith('/api/ui/accounting/periods/'));
  });
});
