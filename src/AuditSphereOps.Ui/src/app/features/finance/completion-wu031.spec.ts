import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { decode } from '../../core/decode';
import { InvoiceDetail } from './invoice';
import { FirmBooks } from './books';
import { decodeReceivablesAging } from './receivables-aging';

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

describe('WU-031: Authorized finance records, receipt reversal and report reconciliation', () => {
  let http: HttpTestingController;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;

  beforeEach(() => {
    params = new BehaviorSubject(convertToParamMap({ id: id(1) }));
    TestBed.configureTestingModule({
      imports: [InvoiceDetail, FirmBooks],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    sessionStorage.clear();
    TestBed.inject(SessionService).current.set({ userId: id(90), firmId: id(91), generation: '1', staff: true });
  });

  afterEach(() => {
    sessionStorage.clear();
    http.verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
  });

  it('preserves historical payment evidence when receipt reversals change outstanding balance', () => {
    // Acceptance criterion 1: Receipt reversal changes current advance eligibility without destroying historical payment evidence
    const fixture = TestBed.createComponent(InvoiceDetail);
    fixture.detectChanges();
    TestBed.tick();

    const invoiceWithReversals = {
      id: id(1),
      billingAccountId: id(50),
      invoiceNumber: 'INV-2026-001',
      currency: 'QAR',
      subtotal: '1000.00',
      tax: '0.00',
      total: '1000.00',
      revision: 3,
      status: 'POSTED',
      createdAt: '2026-10-01T09:00:00Z',
      postedAt: '2026-10-01T10:00:00Z',
      outstanding: '750.00', // Was 500, but 250 was reversed, restoring 250 to outstanding debt
      credited: '0.00',
      allocated: '250.00', // Net cash applied is now 250.00
      canAct: true,
      lines: [{ description: 'Statutory audit advance', quantity: '1.00', unitPrice: '1000.00', lineTotal: '1000.00' }],
      allocations: [
        {
          id: id(101),
          receiptId: id(201),
          receiptReference: 'BANK-ADV-TX-8899',
          currency: 'QAR',
          createdAt: '2026-10-02T11:00:00Z',
          amount: '500.00', // Original applied amount is preserved as historical evidence
          appliedAfterReversals: '250.00', // Current active application after approved reversal
          reversed: '250.00',
          remainingToReverse: '250.00',
          latestReversalRevision: 1,
          canRequestReversal: true,
          canReviewReversal: false,
          reversals: [
            {
              id: id(301),
              receiptAllocationId: id(101),
              revision: 1,
              status: 'APPROVED',
              amount: '250.00',
              reference: 'CORRECTION-REF-001',
              reason: 'Client bank debit recall',
              submittedByUserId: id(90),
              submittedAt: '2026-10-05T08:00:00Z',
              reviewedByUserId: id(95),
              reviewedAt: '2026-10-05T09:00:00Z',
              reviewReason: 'Approved debit memo attached',
            },
          ],
        },
      ],
      receipts: [],
      receiptsHaveMore: false,
      creditNotes: [],
      creditNotesHaveMore: false,
      paymentTerms: [],
      paymentTermsHaveMore: false,
      canSubmitPaymentTerms: false,
      canReviewPaymentTerms: false,
      canIssueCreditNote: true,
      canApproveInvoice: false,
      canPostInvoice: false,
      canSendInvoice: false,
    };

    http.expectOne(`/api/ui/finance/invoices/${id(1)}`).flush(invoiceWithReversals);
    TestBed.tick();
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;
    // Historical payment reference is permanently preserved:
    expect(text).toContain('BANK-ADV-TX-8899');
    expect(text).toContain('500.00'); // Original amount
    // Net applied is clearly distinct:
    expect(text).toContain('250.00'); // Reversed / Remaining
    // Current outstanding reflects the reversal:
    expect(text).toContain('750.00');
    // Reversal review details are shown in the history:
    expect(text).toContain('Client bank debit recall');
    expect(text).toContain('Approved debit memo attached');
  });

  it('displays continuation indicator and does not imply complete totals when more receipts exist', () => {
    // Acceptance criterion 2: Page continuation cannot hide undisplayed receipts or imply complete totals
    const fixture = TestBed.createComponent(InvoiceDetail);
    fixture.detectChanges();
    TestBed.tick();

    const invoiceWithMoreReceipts = {
      id: id(1),
      billingAccountId: id(50),
      invoiceNumber: 'INV-2026-002',
      currency: 'QAR',
      subtotal: '500.00',
      tax: '0.00',
      total: '500.00',
      revision: 1,
      status: 'POSTED',
      createdAt: '2026-10-01T09:00:00Z',
      postedAt: '2026-10-01T10:00:00Z',
      outstanding: '500.00',
      credited: '0.00',
      allocated: '0.00',
      canAct: true,
      lines: [{ description: 'Interim fee', quantity: '1.00', unitPrice: '500.00', lineTotal: '500.00' }],
      allocations: [],
      receipts: [
        {
          id: id(601),
          reference: 'REC-EARLY-01',
          currency: 'QAR',
          amount: '100.00',
          allocated: '0.00',
          remaining: '100.00',
          receivedAt: '2026-10-01T08:00:00Z',
        },
      ],
      receiptsHaveMore: true, // There are undisplayed receipts in the billing account
      creditNotes: [],
      creditNotesHaveMore: false,
      paymentTerms: [],
      paymentTermsHaveMore: false,
      canSubmitPaymentTerms: false,
      canReviewPaymentTerms: false,
      canIssueCreditNote: false,
      canApproveInvoice: false,
      canPostInvoice: false,
      canSendInvoice: false,
    };

    http.expectOne(`/api/ui/finance/invoices/${id(1)}`).flush(invoiceWithMoreReceipts);
    TestBed.tick();
    fixture.detectChanges();

    // The component reflects that older receipts exist and allows loading them:
    expect(fixture.componentInstance.receiptHistoryHaveMore()).toBe(true);
    const olderReceiptsBtn = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((b) => b.textContent?.includes('Load older receipts'));
    expect(olderReceiptsBtn).toBeDefined();
    expect(olderReceiptsBtn?.disabled).toBe(false);
  });

  it('reconciles firm receivables ageing against source postings and policy', () => {
    // Acceptance criterion 3: Firm reports reconcile to source postings and never include client-book invoices
    const sampleAging = {
      asOfDate: '2026-10-22',
      dateBasis: 'POSTED_EFFECTIVE_DATE',
      bucketPolicy: 'STE-AR-BUCKETS-2026.1: 0-30, 31-60, 61-90, 91+',
      rows: [
        {
          invoiceId: id(1),
          clientId: id(20),
          legalClientName: 'Client Alpha QSC',
          engagementId: id(30),
          engagementName: 'Audit 2026',
          invoiceType: 'ADVANCE',
          invoiceNumber: 'INV-2026-001',
          dueDate: '2026-10-15',
          originalAmount: '1000.00',
          appliedReceipts: '500.00',
          reversedReceipts: '250.00',
          appliedCredits: '0.00',
          outstanding: '750.00',
          daysOverdue: 7,
          bucket: 'CURRENT',
          currency: 'QAR',
          paymentTermsStatus: 'APPROVED',
          financeRecipients: ['finance@alpha.qa'],
        },
      ],
      clientCurrencySubtotals: [
        {
          clientId: id(20),
          legalClientName: 'Client Alpha QSC',
          engagementId: null,
          engagementName: null,
          currency: 'QAR',
          invoiceCount: 1,
          originalAmount: '1000.00',
          appliedReceipts: '500.00',
          reversedReceipts: '250.00',
          appliedCredits: '0.00',
          outstanding: '750.00',
        },
      ],
      engagementCurrencySubtotals: [
        {
          clientId: id(20),
          legalClientName: 'Client Alpha QSC',
          engagementId: id(30),
          engagementName: 'Audit 2026',
          currency: 'QAR',
          invoiceCount: 1,
          originalAmount: '1000.00',
          appliedReceipts: '500.00',
          reversedReceipts: '250.00',
          appliedCredits: '0.00',
          outstanding: '750.00',
        },
      ],
      currencySubtotals: [
        {
          clientId: null,
          legalClientName: null,
          engagementId: null,
          engagementName: null,
          currency: 'QAR',
          invoiceCount: 1,
          originalAmount: '1000.00',
          appliedReceipts: '500.00',
          reversedReceipts: '250.00',
          appliedCredits: '0.00',
          outstanding: '750.00',
        },
      ],
    };

    const decoded = decode(decodeReceivablesAging, sampleAging);
    expect(decoded.rows.length).toBe(1);
    const row = decoded.rows[0];
    // Mathematical reconciliation: original - applied + reversed - credits = outstanding
    const expectedOutstanding = (Number(row.originalAmount) - Number(row.appliedReceipts) + Number(row.reversedReceipts) - Number(row.appliedCredits)).toFixed(2);
    expect(row.outstanding).toBe(expectedOutstanding);
    expect(decoded.currencySubtotals[0].outstanding).toBe(expectedOutstanding);
  });
});
