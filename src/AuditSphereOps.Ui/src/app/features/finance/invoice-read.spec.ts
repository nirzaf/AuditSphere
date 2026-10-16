import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { InvoiceDetail } from './invoice';

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const allocation = (n: number) => ({ id: id(100 + n), receiptId: id(200 + n), receiptReference: `BANK-${n}`, currency: 'QAR',
  createdAt: '2026-10-01T09:15:00Z', amount: '1.25', appliedAfterReversals: '1.25', reversed: '0.00', remainingToReverse: '1.25',
  latestReversalRevision: 0, canRequestReversal: false, canReviewReversal: false, reversals: [] });
const invoice = (count = 26, n = 1) => ({ id: id(n), billingAccountId: id(50), invoiceNumber: `SYN-INVOICE-${n}`, currency: 'QAR',
  subtotal: '100.00', tax: '0.00', total: '100.00', revision: 1, status: 'APPROVED', createdAt: '2026-10-01T09:00:00Z', postedAt: null,
  outstanding: '67.50', credited: '0.00', allocated: '32.50', canAct: true,
  lines: [{ description: 'Synthetic confidential line', quantity: '1.00', unitPrice: '100.00', lineTotal: '100.00' }],
  allocations: Array.from({ length: count }, (_, i) => allocation(i)), receipts: [], receiptsHaveMore: false,
  creditNotes: [], creditNotesHaveMore: false, paymentTerms: [], paymentTermsHaveMore: false,
  canSubmitPaymentTerms: false, canReviewPaymentTerms: false, canIssueCreditNote: false,
  canApproveInvoice: false, canPostInvoice: true, canSendInvoice: false });

describe('Invoice exact read and allocation pagination', () => {
  let http: HttpTestingController;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    params = new BehaviorSubject(convertToParamMap({ id: id(1) }));
    TestBed.configureTestingModule({ imports: [InvoiceDetail], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: ActivatedRoute, useValue: { paramMap: params } }] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: id(90), firmId: id(91), generation: '1', staff: true });
  });
  afterEach(() => { http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); });
  function open(payload = invoice()) {
    const fixture = TestBed.createComponent(InvoiceDetail); fixture.detectChanges(); TestBed.tick();
    http.expectOne(`/api/ui/finance/invoices/${id(1)}`).flush(payload); TestBed.tick(); return fixture;
  }
  it('rejects a wrong returned identity through HTTP without rendering invoice content or actions', () => {
    const f = open(invoice(1, 2));
    expect(f.componentInstance.invoice.data()).toBeNull();
    expect(f.componentInstance.invoice.error()).not.toBe('');
    expect(f.nativeElement.textContent).not.toContain('SYN-INVOICE-2');
    expect(f.nativeElement.textContent).not.toContain('Synthetic confidential line');
    expect(f.nativeElement.textContent).not.toContain('67.50');
    expect(f.nativeElement.textContent).not.toContain('Post invoice');
    expect(f.nativeElement.querySelector('section[aria-labelledby="workflow-heading"]')).toBeNull();
    http.expectNone(r => r.method !== 'GET');
  });
  it('pages allocations with legacy sizes while preserving whole-invoice amounts and exact receipt identity', () => {
    const f = open(); const c = f.componentInstance;
    // Locate the allocation table by its exact caption, independently of other billing histories.
    const allocationRows = () => Array.from(f.nativeElement.querySelectorAll('table') as NodeListOf<HTMLTableElement>)
      .find(t => t.querySelector('caption')?.textContent === 'Receipt allocations and independently reviewed reversals for this invoice')!
      .querySelectorAll('tbody tr');
    expect(allocationRows().length).toBe(10);
    expect(allocationRows()[0].textContent).toContain(id(200));
    expect(allocationRows()[0].textContent).toContain('BANK-0');
    expect(allocationRows()[0].textContent).toContain('2026-10-01 09:15 UTC');
    const summary = () => Array.from(f.nativeElement.querySelectorAll('p') as NodeListOf<HTMLParagraphElement>)
      .find(p => p.textContent?.includes('original total'))!.textContent;
    expect(summary()).toContain('26 allocations · original total 32.50 QAR');
    c.nextAllocationPage(26); TestBed.tick(); expect(allocationRows().length).toBe(10);
    expect(allocationRows()[0].textContent).toContain('BANK-10');
    c.nextAllocationPage(26); TestBed.tick(); expect(allocationRows().length).toBe(6);
    expect(summary()).toContain('32.50'); expect(f.nativeElement.textContent).toContain('balance outstanding 67.50');
    const select = f.nativeElement.querySelector('select[aria-label="Allocations per page"]') as HTMLSelectElement;
    expect(Array.from(select.options).map(o => Number(o.value))).toEqual([10, 25, 50]);
    for (const [size, count] of [[25, 25], [50, 26]]) {
      select.value = String(size); select.dispatchEvent(new Event('change')); TestBed.tick();
      expect(c.allocationPageIndex()).toBe(0); expect(allocationRows().length).toBe(count); expect(summary()).toContain('32.50');
    }
    http.expectNone(r => r.method !== 'GET');
  });
  it.each([403, 503])('clears allocation identities and actions after a %s refresh failure', async status => {
    const f = open();
    await f.componentInstance.refresh(); TestBed.tick();
    http.expectOne(`/api/ui/finance/invoices/${id(1)}`).flush({}, { status, statusText: 'Unavailable' }); TestBed.tick();
    expect(f.componentInstance.invoice.data()).toBeNull();
    expect(f.nativeElement.textContent).not.toContain(id(200));
    expect(f.nativeElement.textContent).not.toContain('BANK-0');
    expect(f.nativeElement.textContent).not.toContain('Post invoice');
    http.expectNone(r => r.method !== 'GET');
  });
  it('resets paging on explicit refresh and authorization result replacement while retaining the selected size', async () => {
    const f = open(); const c = f.componentInstance;
    c.setAllocationPageSize({ target: { value: '25' } } as unknown as Event);
    c.nextAllocationPage(26); TestBed.tick(); expect(c.allocationPageIndex()).toBe(1);
    await c.refresh(); TestBed.tick(); expect(c.invoice.data()).toBeNull();
    http.expectOne(`/api/ui/finance/invoices/${id(1)}`).flush(invoice()); TestBed.tick();
    expect(c.allocationPageIndex()).toBe(0); expect(c.allocationPageSize()).toBe(25);
    c.nextAllocationPage(26); TestBed.tick();
    TestBed.inject(SessionService).invalidation.update(n => n + 1); TestBed.tick();
    expect(c.invoice.data()).toBeNull();
    http.expectOne(`/api/ui/finance/invoices/${id(1)}`).flush(invoice(1)); TestBed.tick();
    expect(c.allocationPageIndex()).toBe(0); expect(c.allocationPage(invoice(1).allocations).length).toBe(1);
    params.next(convertToParamMap({ id: id(2) })); TestBed.tick();
    http.expectOne(`/api/ui/finance/invoices/${id(2)}`).flush(invoice(1, 2)); TestBed.tick();
    expect(c.allocationPageIndex()).toBe(0); expect(f.nativeElement.textContent).not.toContain('SYN-INVOICE-1');
    TestBed.inject(SessionService).clear(); TestBed.tick();
    expect(c.invoice.data()).toBeNull(); expect(f.nativeElement.textContent).not.toContain('BANK-0');
    http.expectNone(r => r.method !== 'GET');
  });
});
