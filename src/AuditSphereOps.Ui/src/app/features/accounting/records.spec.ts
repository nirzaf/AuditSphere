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
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AccountingRecords],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: ActivatedRoute, useValue: { data: new BehaviorSubject({ mode: 'mappings' }) } }],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: id(1), firmId: id(2), generation: '1', staff: true });
  });
  afterEach(() => { http.verify(); TestBed.resetTestingModule(); });

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
