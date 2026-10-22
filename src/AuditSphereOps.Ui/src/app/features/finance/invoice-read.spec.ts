import { vi } from 'vitest';
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
    sessionStorage.clear();
    TestBed.inject(SessionService).current.set({ userId: id(90), firmId: id(91), generation: '1', staff: true });
  });
  afterEach(() => { sessionStorage.clear(); http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); vi.restoreAllMocks(); });
  function open(payload: object = invoice()) {
    const fixture = TestBed.createComponent(InvoiceDetail); fixture.detectChanges(); TestBed.tick();
    http.expectOne(`/api/ui/finance/invoices/${id(1)}`).flush(payload); TestBed.tick(); return fixture;
  }
  it('states that a SENT invoice is a staff record without delivery evidence', () => {
    const sent = open({ ...invoice(1), status: 'SENT', postedAt: '2026-10-02T09:00:00Z' });
    const note = (sent.nativeElement as HTMLElement).querySelector('[data-testid="invoice-sent-untracked"]');
    expect(note?.textContent).toContain('no delivery or read evidence');
    const posted = open({ ...invoice(1), status: 'POSTED', postedAt: '2026-10-02T09:00:00Z' });
    expect((posted.nativeElement as HTMLElement).querySelector('[data-testid="invoice-sent-untracked"]')).toBeNull();
  });
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
  for (const action of ['approve', 'post', 'send'] as const) {
    it.each([
      { status: 403, code: 'scope.denied' },
      { status: 409, code: 'generation.stale' },
      { status: 401, code: 'request.failed' },
      { status: 403, code: 'request.failed' },
      { status: 404, code: 'request.failed' },
    ])(`clears protected invoice after ${action} authority refusal $status/$code without retrying the command`, async ({ status, code }) => {
      const payload = { ...invoice(), status: action === 'approve' ? 'REVIEW_REQUIRED' : action === 'post' ? 'APPROVED' : 'POSTED',
        canApproveInvoice: action === 'approve', canPostInvoice: action === 'post', canSendInvoice: action === 'send',
        receipts: [{ id: id(600), reference: 'PRIVATE receipt history', currency: 'QAR', amount: '40.00', allocated: '0.00', remaining: '40.00', receivedAt: '2026-10-01T09:00:00Z' }],
        creditNotes: [{ id: id(601), noteNumber: 'PRIVATE credit history', currency: 'QAR', amount: '1.00', reason: 'Private correction', createdAt: '2026-10-01T09:00:00Z' }] };
      const f = open(payload); const c = f.componentInstance;
      // Keep 401 session revalidation controlled so this tests command-authority clearing directly.
      const sessionRefresh = vi.spyOn(TestBed.inject(SessionService), 'refresh').mockResolvedValue(false);
      c.receiptForm.patchValue({ amount: '12', reference: 'PRIVATE unsaved payment', reviewed: true });
      c.selectedAllocation.set(c.invoice.data()!.allocations[0]);
      const operation = c.act(id(1), action, 'Unexpected success');
      http.expectOne(r => r.method === 'POST' && r.url === `/api/ui/finance/invoices/${id(1)}/${action}`)
        .flush({ code, message: 'Current authority refused.' }, { status, statusText: 'Authority unavailable' });
      await operation; TestBed.tick();
      expect(c.invoice.data()).toBeNull(); expect(c.uncertain()).toBe(false); expect(c.failed()).toBe(true);
      expect(c.receiptHistory()).toEqual([]); expect(c.creditHistory()).toEqual([]); expect(c.selectedAllocation()).toBeNull();
      expect(c.receiptForm.controls.reference.value).toBe(''); expect(c.receiptForm.controls.reviewed.value).toBe(false);
      for (const marker of ['SYN-INVOICE-1', 'Synthetic confidential line', '67.50', id(200), 'BANK-0', 'PRIVATE receipt history', 'PRIVATE credit history']) {
        expect(f.nativeElement.textContent).not.toContain(marker);
      }
      expect(f.nativeElement.querySelector('section[aria-labelledby="workflow-heading"]')).toBeNull();
      expect(f.nativeElement.querySelector('nav[aria-label="Allocation pages"]')).toBeNull();
      expect(f.nativeElement.textContent).toContain(status === 409
        ? 'This record changed since you loaded it. Refresh and review the current revision.' : 'Current authority refused.');
      http.expectOne(`/api/ui/finance/invoices/${id(1)}`).flush({}, { status: 403, statusText: 'Forbidden' }); TestBed.tick();
      expect(c.invoice.data()).toBeNull();
      http.expectNone(r => r.method !== 'GET');
      if (status === 401) expect(sessionRefresh).toHaveBeenCalledOnce();
    });
  }

  it('preserves loaded content for a business refusal and preserves fail-closed unknown recovery', async () => {
    const f = open(); const c = f.componentInstance;
    const refused = c.act(id(1), 'post', 'Unexpected success');
    http.expectOne(`/api/ui/finance/invoices/${id(1)}/post`)
      .flush({ code: 'state.protected', message: 'Business prerequisite missing.' }, { status: 409, statusText: 'Conflict' });
    await refused; TestBed.tick();
    expect(c.invoice.data()?.id).toBe(id(1)); expect(c.uncertain()).toBe(false);
    http.expectNone(r => r.method === 'GET');
    const unknown = c.act(id(1), 'post', 'Unexpected success');
    http.expectOne(`/api/ui/finance/invoices/${id(1)}/post`).error(new ProgressEvent('error'));
    await unknown; TestBed.tick();
    expect(c.invoice.data()?.id).toBe(id(1)); expect(c.uncertain()).toBe(true);
    await c.act(id(1), 'post', 'Unexpected retry');
    http.expectNone(r => r.method !== 'GET'); http.expectNone(r => r.method === 'GET');
  });

  it('restores after reload and retries only the retained billing request after an unknown outcome', async () => {
    const f = open(); const original = f.componentInstance;
    original.receiptForm.patchValue({ amount: '12.00', reference: 'SYN-RETRY-001', reviewed: true });
    const firstAttempt = original.recordReceipt(original.invoice.data()!);
    const first = http.expectOne(r => r.method === 'POST' && r.url === `/api/ui/finance/billing-accounts/${id(50)}/receipts`);
    const originalBody = first.request.body as { requestId: string; amount: string; reference: string; reviewed: boolean };
    first.error(new ProgressEvent('error'));
    await firstAttempt; TestBed.tick();
    expect(original.uncertain()).toBe(true);
    expect(original.pendingBillingCommand()?.requestId).toBe(originalBody.requestId);
    f.destroy();

    const restoredFixture = TestBed.createComponent(InvoiceDetail);
    restoredFixture.detectChanges(); TestBed.tick();
    const c = restoredFixture.componentInstance;
    expect(c.pendingBillingCommand()).toBeNull();
    http.expectOne(`/api/ui/finance/invoices/${id(1)}`).flush(invoice()); TestBed.tick();
    expect(c.uncertain()).toBe(true);
    expect(c.pendingBillingCommand()?.requestId).toBe(originalBody.requestId);

    await c.act(id(1), 'post', 'Must not replace unresolved payment.');
    http.expectNone(r => r.method === 'POST');

    const retryAttempt = c.retryPendingBillingCommand();
    const retry = http.expectOne(r => r.method === 'POST' && r.url === `/api/ui/finance/billing-accounts/${id(50)}/receipts`);
    expect(retry.request.body).toEqual(originalBody);
    retry.flush({ value: id(700) });
    await retryAttempt; TestBed.tick();
    http.expectOne(`/api/ui/finance/invoices/${id(1)}`).flush(invoice()); TestBed.tick();
    expect(c.uncertain()).toBe(false);
    expect(c.pendingBillingCommand()).toBeNull();
    expect(c.message()).toContain('Receipt recorded');
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

  it.each(['success', 'refusal', 'unknown'] as const)('resets drafts across A to B to A visits and ignores a delayed %s command from the old visit', async outcome => {
    const f = open(); const c = f.componentInstance;
    c.receiptForm.patchValue({ amount: '12', reference: 'PRIVATE invoice A', reviewed: true });
    const oldCommand = c.act(id(1), 'post', 'Old invoice A command completed.');
    const command = http.expectOne(`/api/ui/finance/invoices/${id(1)}/post`);

    params.next(convertToParamMap({ id: id(2) })); TestBed.tick();
    http.expectOne(`/api/ui/finance/invoices/${id(2)}`).flush(invoice(1, 2)); TestBed.tick();
    expect(c.receiptForm.controls.reference.value).toBe('');
    expect(c.receiptForm.controls.reviewed.value).toBe(false);
    params.next(convertToParamMap({ id: id(1) })); TestBed.tick();
    http.expectOne(`/api/ui/finance/invoices/${id(1)}`).flush(invoice()); TestBed.tick();
    if (outcome === 'success') command.flush({ value: {} });
    else if (outcome === 'refusal') command.flush({ code: 'state.protected', message: 'Protected.' }, { status: 409, statusText: 'Conflict' });
    else command.error(new ProgressEvent('error'));
    await oldCommand; TestBed.tick();
    expect(c.invoice.data()?.id).toBe(id(1));
    expect(c.message()).not.toBe('Old invoice A command completed.');
    expect(c.receiptForm.controls.reference.value).toBe('');
    expect(c.receiptForm.controls.reviewed.value).toBe(false);
    expect(c.uncertain()).toBe(false);
    expect(c.pendingBillingCommand()).toBeNull();
  });

  it('clears review assent when billing inputs change or the current payment-terms revision changes', () => {
    const payload = { ...invoice(), paymentTerms: [{ id: id(401), revision: 1, dueDate: '2026-12-31', basis: 'CONTRACTUAL_DUE_DATE',
      termsDescription: 'NET 30', evidenceReference: 'ENGAGEMENT-REV-1', status: 'PENDING_REVIEW', submittedByUserId: id(90),
      submittedAt: '2026-10-01T09:00:00Z', reviewedByUserId: null, reviewedAt: null, reviewReason: null }], canReviewPaymentTerms: true };
    const f = open(payload); const c = f.componentInstance;
    c.receiptForm.patchValue({ amount: '12', reference: 'SYN-RECEIPT-1', reviewed: true });
    expect(c.receiptForm.controls.reviewed.value).toBe(true);
    c.receiptForm.controls.amount.setValue('13');
    expect(c.receiptForm.controls.reviewed.value).toBe(false);
    c.receiptForm.controls.reviewed.setValue(true);
    c.receiptForm.controls.reference.setValue('SYN-RECEIPT-2');
    expect(c.receiptForm.controls.reviewed.value).toBe(false);

    c.termsReviewForm.patchValue({ reason: 'Verified exact terms.', reviewed: true });
    expect(c.termsReviewForm.controls.reviewed.value).toBe(true);
    c.termsForm.patchValue({ dueDate: '2026-12-31', basis: 'CONTRACTUAL_DUE_DATE', termsDescription: 'NET 30',
      evidenceReference: 'ENGAGEMENT-REV-1', reviewed: true });
    c.invoice.reload(); TestBed.tick();
    http.expectOne(`/api/ui/finance/invoices/${id(1)}`).flush({ ...payload, paymentTerms: [{ ...payload.paymentTerms[0], id: id(402), revision: 2 }] }); TestBed.tick();
    expect(c.termsReviewForm.controls.reviewed.value).toBe(false);
    expect(c.termsReviewForm.controls.reason.value).toBe('');
    expect(c.termsForm.controls.reviewed.value).toBe(false);
    expect(c.termsForm.controls.termsDescription.value).toBe('NET 30');
  });
});
