import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { AccountingEvidence } from './evidence';

const id = (value: number) => `00000000-0000-4000-8000-${value.toString().padStart(12, '0')}`;

describe('accounting evidence queue pagination', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AccountingEvidence],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: id(1), firmId: id(2), generation: '1', staff: true });
  });
  afterEach(() => { http.verify(); TestBed.resetTestingModule(); });

  it('matches the legacy 10, 25 and 50 rows-per-page choices and resets to page one', () => {
    const fixture = TestBed.createComponent(AccountingEvidence);
    fixture.detectChanges();
    http.expectOne('/api/ui/accounting/evidence').flush(Array.from({ length: 26 }, (_, i) => ({
      kind: 'ECL', area: 'CASH', clientName: `Synthetic client ${i}`, engagementName: 'Synthetic engagement', periodCode: 'FY26',
      status: 'PREPARED', inputGeneration: 1, currentGeneration: 1, linkStatus: 'LINK_PENDING', workpaperId: null,
      reference: 'synthetic reference', isStale: false, isTerminal: false, evidenceId: id(i + 100),
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

    const next = fixture.nativeElement.querySelector('nav[aria-label="Pages"] button:nth-of-type(2)') as HTMLButtonElement;
    next.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('nav[aria-label="Pages"]').textContent).toContain('Page 2 of 3');

    select.value = '50';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(26);
    expect(fixture.nativeElement.querySelector('nav[aria-label="Pages"]').textContent).toContain('Page 1 of 1');
  });
});
