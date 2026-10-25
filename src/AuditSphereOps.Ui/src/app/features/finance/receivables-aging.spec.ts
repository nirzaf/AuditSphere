import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { FirmReceivablesAging } from './receivables-aging';

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const row = (n: number, client: number, name: string, dueDate: string | null) => ({
  invoiceId: id(n), clientId: id(client), legalClientName: name, engagementId: null, engagementName: null, invoiceType: 'TIME',
  invoiceNumber: `SYN-${n}`, dueDate, originalAmount: '100.00', appliedReceipts: '0.00', reversedReceipts: '0.00',
  appliedCredits: '0.00', outstanding: '100.00', daysOverdue: dueDate ? 5 : null, bucket: dueDate ? 'OVERDUE_1_30' : 'UNDATED',
  currency: 'QAR', paymentTermsStatus: dueDate ? 'APPROVED' : 'NOT_CONFIGURED', financeRecipients: [],
});
const subtotal = { clientId: null, legalClientName: null, engagementId: null, engagementName: null, currency: 'QAR', invoiceCount: 2,
  originalAmount: '200.00', appliedReceipts: '0.00', reversedReceipts: '0.00', appliedCredits: '0.00', outstanding: '200.00' };

describe('FirmReceivablesAging display filter', () => {
  it('narrows only the invoice table and leaves the complete subtotals in place', () => {
    TestBed.configureTestingModule({ imports: [FirmReceivablesAging], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.inject(SessionService).current.set({ userId: id(90), firmId: id(91), generation: '1', staff: true });
    const fixture = TestBed.createComponent(FirmReceivablesAging);
    const http = TestBed.inject(HttpTestingController);
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne(request => request.url.startsWith('/api/ui/finance/receivables-aging?asOf=')).flush({
        asOfDate: '2026-10-25', dateBasis: 'UTC dates.', bucketPolicy: 'Policy.',
        rows: [row(1, 10, 'Beta client', '2026-10-20'), row(2, 11, 'Alpha client', null)],
        clientCurrencySubtotals: [], engagementCurrencySubtotals: [], currencySubtotals: [subtotal],
      });
      fixture.detectChanges();
      const host = fixture.nativeElement as HTMLElement;
      const detailRows = () => [...host.querySelectorAll('section[aria-labelledby="receivables-heading"] tbody tr')];
      expect(detailRows()).toHaveLength(2);
      expect(detailRows()[1].textContent).toContain('Undated · review terms');
      const select = host.querySelector('section[aria-labelledby="receivables-heading"] select') as HTMLSelectElement;
      expect([...select.options].map(o => o.textContent?.trim())).toEqual(['All clients in this report', 'Alpha client', 'Beta client']);
      select.value = id(10); select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      expect(detailRows()).toHaveLength(1);
      expect(host.textContent).toContain('Showing 1 of 2 invoices.');
      expect(host.querySelector('section[aria-labelledby="currency-subtotals-heading"] tbody')?.textContent).toContain('200.00');
      http.expectNone(request => request.method !== 'GET');
      http.verify();
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });
});
