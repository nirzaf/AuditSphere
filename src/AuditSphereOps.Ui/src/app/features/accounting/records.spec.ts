import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { AccountingRecords } from './records';

const id = (value: number) => `00000000-0000-4000-8000-${value.toString().padStart(12, '0')}`;

describe('accounting record queue pagination', () => {
  let http: HttpTestingController;
  let routeData: BehaviorSubject<{ mode: string }>;
  beforeEach(() => {
    routeData = new BehaviorSubject({ mode: 'mappings' });
    TestBed.configureTestingModule({
      imports: [AccountingRecords],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: ActivatedRoute, useValue: { data: routeData } }],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: id(1), firmId: id(2), generation: '1', staff: true });
  });
  afterEach(() => { http.verify(); TestBed.resetTestingModule(); });

  const modes = ['mappings', 'journals', 'differences'] as const;
  function rows(mode: typeof modes[number], count: number) {
    return Array.from({ length: count }, (_, i) => {
      const common = { id: id(i + 100), clientName: `Synthetic client ${i}`, engagementName: 'Synthetic engagement' };
      if (mode === 'mappings') return { ...common, periodStart: '2026-01-01', periodEnd: '2026-12-31', version: 1, generation: 1, hasChart: true, status: 'APPROVED' };
      if (mode === 'journals') return { ...common, journalNumber: `J-${i}`, purpose: 'Synthetic journal', periodCode: 'FY2026', currency: 'QAR', status: 'Posted', revision: 1 };
      return { ...common, accountArea: 'Synthetic difference', differenceType: 'FACTUAL', amount: '10.00', currency: 'QAR', status: 'OPEN', correctionState: 'LINKED', proposedJournalId: id(900) };
    });
  }
  for (const mode of modes) {
    it(`resets ${mode} pagination when authorization invalidation shrinks a populated queue`, () => {
      routeData.next({ mode });
      const fixture = TestBed.createComponent(AccountingRecords);
      fixture.detectChanges();
      const endpoint = '/api/ui/accounting/' + mode;
      http.expectOne(endpoint).flush(rows(mode, 26)); TestBed.tick();
      const c = fixture.componentInstance;
      c.setPageSize({ target: { value: '10' } } as unknown as Event);
      c.next(26); c.next(26); TestBed.tick();
      expect(fixture.nativeElement.textContent).toContain('Page 3 of 3');
      TestBed.inject(SessionService).invalidation.update(n => n + 1); TestBed.tick();
      expect(fixture.nativeElement.querySelector('tbody')).toBeNull();
      http.expectOne(endpoint).flush(rows(mode, 1)); TestBed.tick();
      expect(c.pageIndex()).toBe(0); expect(c.pageSize()).toBe(10);
      expect(fixture.nativeElement.textContent).toContain('Page 1 of 1');
      expect(fixture.nativeElement.querySelectorAll('tbody tr')).toHaveLength(1);
      const href = fixture.nativeElement.querySelector('tbody a').getAttribute('href');
      expect(href).toBe(mode === 'differences' ? '/app/accounting/journals/' + id(900) : '/app/accounting/' + mode + '/' + id(100));
      expect(fixture.nativeElement.textContent).toContain(mode === 'mappings' ? '1 chart-bound' : mode === 'journals' ? '1 posted' : '1 linked journals');
      http.expectNone(r => r.method === 'POST');
    });
    it(`clears ${mode} rows and links on transient failure and restores a first page on retry`, () => {
      routeData.next({ mode });
      const fixture = TestBed.createComponent(AccountingRecords); fixture.detectChanges();
      const endpoint = '/api/ui/accounting/' + mode;
      http.expectOne(endpoint).flush(rows(mode, 26)); TestBed.tick();
      const c = fixture.componentInstance;
      c.next(26); TestBed.tick(); expect(c.pageIndex()).toBe(1);
      ({ mappings: c.mappings, journals: c.journals, differences: c.differences })[mode].reload(); TestBed.tick();
      http.expectOne(endpoint).flush({}, { status: 503, statusText: 'Unavailable' }); TestBed.tick();
      expect(fixture.nativeElement.querySelector('tbody')).toBeNull();
      expect(fixture.nativeElement.textContent).not.toContain('Synthetic client');
      expect(fixture.nativeElement.textContent).toContain('Temporarily unavailable. Retry shortly.');
      c.reload(); http.expectOne(endpoint).flush(rows(mode, 1)); TestBed.tick();
      expect(c.pageIndex()).toBe(0); expect(fixture.nativeElement.textContent).toContain('Page 1 of 1');
      expect(fixture.nativeElement.querySelectorAll('tbody tr')).toHaveLength(1);
      http.expectNone(r => r.method === 'POST');
    });
  }

  it('matches the legacy row-count options for the selected record queue', () => {
    const fixture = TestBed.createComponent(AccountingRecords);
    fixture.detectChanges();
    http.expectOne('/api/ui/accounting/mappings').flush(Array.from({ length: 26 }, (_, i) => ({
      id: id(i + 100), clientName: `Synthetic client ${i}`, engagementName: 'Synthetic engagement',
      periodStart: '2026-01-01', periodEnd: '2026-12-31', version: 1, generation: 1, hasChart: true, status: 'APPROVED',
    })));
    fixture.detectChanges();

    const select = fixture.nativeElement.querySelector('nav[aria-label="Pages"] select') as HTMLSelectElement;
    expect(Array.from(select.options).map(option => Number(option.value))).toEqual([10, 25, 50]);
    expect(select.value).toBe('25');
    expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(25);
    select.value = '10';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(10);
    expect(fixture.nativeElement.querySelector('nav[aria-label="Pages"]').textContent).toContain('Page 1 of 3');
    select.value = '50';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(26);
    expect(fixture.nativeElement.querySelector('nav[aria-label="Pages"]').textContent).toContain('Page 1 of 1');
  });
});
