import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { decodeInvoicePage, FirmInvoices } from './invoices';

const client = '11111111-1111-4111-8111-111111111111';
const invoice = (id: string, overrides: Record<string, unknown> = {}) => ({
  id, invoiceNumber: 'INV-100', clientId: client, clientName: 'Billing Client', kind: 'OTHER', engagementId: null, engagementName: null,
  multipleEngagements: false, currency: 'QAR', status: 'SENT', revision: 3, createdAt: '2026-10-20T08:00:00+00:00',
  postedAt: '2026-10-21T08:00:00+00:00', sentAt: '2026-10-22T08:00:00+00:00', total: '220.00', credited: '20.00', allocated: '200.00',
  outstanding: '0.00', approvedDueDate: null, paymentTermsPending: false, ...overrides,
});
const pageOf = () => ({
  page: 0, pageSize: 25, total: 3,
  items: [
    invoice('22222222-2222-4222-8222-222222222222'),
    invoice('33333333-3333-4333-8333-333333333333', { invoiceNumber: 'INV-101', status: 'DRAFT', postedAt: null, sentAt: null,
      total: '50.00', credited: '0', allocated: '0', outstanding: '50.00', kind: 'TIME', multipleEngagements: true }),
    invoice('44444444-4444-4444-8444-444444444444', { invoiceNumber: 'INV-200', currency: 'USD', status: 'POSTED', sentAt: null,
      total: '1000.00', credited: '0', allocated: '400.00', outstanding: '600.00', approvedDueDate: '2026-11-30', paymentTermsPending: true }),
  ],
  totals: [
    { currency: 'QAR', basis: 'POSTED', count: 1, total: '220.00', credited: '20.00', allocated: '200.00', outstanding: '0.00' },
    { currency: 'QAR', basis: 'UNPOSTED', count: 1, total: '50.00', credited: null, allocated: null, outstanding: null },
    { currency: 'USD', basis: 'POSTED', count: 1, total: '1000.00', credited: '0', allocated: '400.00', outstanding: '600.00' },
  ],
});

describe('invoice page decoder', () => {
  it('requires totals to account for every matching invoice and keeps settlement off unposted value', () => {
    expect(decodeInvoicePage(pageOf(), 'p').totals).toHaveLength(3);
    expect(() => decodeInvoicePage({ ...pageOf(), total: 4 }, 'p')).toThrow();
    const value = pageOf();
    expect(() => decodeInvoicePage({ ...value, totals: [value.totals[0], { ...value.totals[1], outstanding: '50.00' }, value.totals[2]] }, 'p')).toThrow();
    expect(() => decodeInvoicePage({ ...value, items: [{ ...value.items[0], sentAt: null }, ...value.items.slice(1)] }, 'p')).toThrow();
  });
});

describe('FirmInvoices', () => {
  it('separates posted settlement from unposted value, keeps the sent disclosure and filters by client', () => {
    TestBed.configureTestingModule({ imports: [FirmInvoices], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.inject(SessionService).current.set({ userId: client, firmId: client, generation: '1', staff: true });
    const fixture = TestBed.createComponent(FirmInvoices);
    const http = TestBed.inject(HttpTestingController);
    try {
      fixture.detectChanges(); TestBed.tick();
      http.expectOne('/api/ui/finance/invoices').flush(pageOf());
      fixture.detectChanges();
      const host = fixture.nativeElement as HTMLElement;
      const totals = [...host.querySelectorAll('table')[0].querySelectorAll('tbody tr')].map(r => r.textContent?.replace(/\s+/g, ' ').trim());
      expect(totals[1]).toContain('Not yet posted (draft, review, approved)');
      expect(totals[1]).toContain('Not receivable');
      expect(totals[2]).toContain('USD');
      expect(host.textContent).toContain('Complete for all 3 invoices matching the filters, not only this page.');
      const rows = [...host.querySelectorAll('table')[1].querySelectorAll('tbody tr')].map(r => r.textContent?.replace(/\s+/g, ' ').trim());
      expect(rows[0]).toContain('Recorded as sent by staff; no dispatch or delivery evidence.');
      expect(rows[0]).toContain('No balance outstanding');
      expect(rows[1]).toContain('Multiple engagements');
      expect(rows[1]).not.toContain('outstanding');
      expect(rows[1]).toContain('Not receivable');
      expect(rows[1]).not.toContain('50.00 No approved terms');
      expect(rows[2]).toContain('Partially settled');
      expect(rows[2]).toContain('Terms revision pending review');
      expect(host.querySelector('a[href="/app/practice/invoices/22222222-2222-4222-8222-222222222222"]')).not.toBeNull();

      host.querySelector<HTMLButtonElement>('button[aria-label="Show only invoices for Billing Client"]')!.click();
      fixture.componentInstance.draft.status = 'POSTED';
      fixture.componentInstance.apply();
      TestBed.tick();
      const filtered = http.expectOne(request => request.url.startsWith('/api/ui/finance/invoices?'));
      const params = new URL(filtered.request.url, 'http://localhost').searchParams;
      expect([params.get('clientId'), params.get('status'), params.has('page')]).toEqual([client, 'POSTED', false]);
      filtered.flush({ ...pageOf(), items: [], total: 0, totals: [] });
      http.verify({ ignoreCancelled: true });
    } finally { fixture.destroy(); TestBed.resetTestingModule(); }
  });
});
