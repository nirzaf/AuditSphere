import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { AccountingPeriodRecord } from './period';

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
  revision: 2,
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
  function read(value: object, n = 1) {
    http.expectOne('/api/ui/accounting/periods/' + id(n)).flush(value);
    TestBed.tick();
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
    http.expectNone((r) => r.method !== 'GET');
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
    read({ ...record(2), clientName: 'Do not display wrong identity' });
    expect(fixture.componentInstance.period.data()).toBeNull();
    expect(fixture.componentInstance.period.error()).not.toBe('');
    expect(fixture.nativeElement.textContent).not.toContain('Do not display wrong identity');
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
    read({ ...record(), revision: 3 });
    expect(fixture.componentInstance.period.data()?.revision).toBe(3);
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
