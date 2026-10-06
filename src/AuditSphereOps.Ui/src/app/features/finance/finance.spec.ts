import { describe, expect, it } from 'vitest';
import { decodeLedger } from './ledger';
import { decodeInvoice } from './invoice';
import { decodeBooks, decodeTrialBalance } from './books';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const hash64 = 'b'.repeat(64);

describe('Finance & Firm Ledger Contracts', () => {
  it('decodes a valid firm ledger payload', () => {
    const raw = {
      canClosePeriod: true,
      periods: [
        {
          id: id1,
          periodCode: 'FY2026',
          status: 'OPEN',
          revision: 1,
          closedAt: null,
        },
      ],
      accounts: [
        {
          id: id2,
          code: '1000',
          name: 'Operating Cash',
          accountType: 'ASSET',
          normalSide: 'DEBIT',
          postingAllowed: true,
        },
      ],
      postings: [
        {
          id: id1,
          postedAt: '2026-10-01T08:00:00Z',
          currency: 'QAR',
          postedByUserId: id2,
          reversalOfPostingId: null,
        },
      ],
    };

    const decoded = decodeLedger(raw, 'ledger');
    expect(decoded.canClosePeriod).toBe(true);
    expect(decoded.periods.length).toBe(1);
    expect(decoded.accounts[0].code).toBe('1000');
    expect(decoded.postings[0].currency).toBe('QAR');
  });

  it('decodes a valid invoice payload', () => {
    const raw = {
      id: id1,
      invoiceNumber: 'INV-2026-001',
      currency: 'QAR',
      subtotal: '50000.00',
      tax: '0.00',
      total: '50000.00',
      revision: 1,
      status: 'APPROVED',
      createdAt: '2026-10-01T09:00:00Z',
      postedAt: null,
      outstanding: '50000.00',
      credited: '1000.00',
      allocated: '0.00',
      canAct: true,
      billingAccountId: id2,
      lines: [
        {
          description: 'Interim statutory audit services',
          quantity: '1.00',
          unitPrice: '50000.00',
          lineTotal: '50000.00',
        },
      ],
      allocations: [],
      receipts: [{ id: id2, reference: 'BANK-001', currency: 'QAR', amount: '5000.00', allocated: '0.00', remaining: '5000.00', receivedAt: '2026-10-01T09:00:00Z' }],
      receiptsHaveMore: false,
      creditNotes: [{ id: '33333333-3333-4333-8333-333333333333', noteNumber: 'CN-001', currency: 'QAR', amount: '1000.00', reason: 'Reviewed adjustment', createdAt: '2026-10-01T10:00:00Z' }],
      creditNotesHaveMore: false,
      canIssueCreditNote: true,
      canApproveInvoice: false,
      canPostInvoice: true,
      canSendInvoice: true,
    };

    const decoded = decodeInvoice(raw, 'invoice');
    expect(decoded.id).toBe(id1);
    expect(decoded.invoiceNumber).toBe('INV-2026-001');
    expect(decoded.lines.length).toBe(1);
    expect(decoded.outstanding).toBe('50000.00');
    expect(decoded.receipts[0].remaining).toBe('5000.00');
    expect(decoded.creditNotes[0].noteNumber).toBe('CN-001');
    expect(decoded.canIssueCreditNote).toBe(true);
    expect(decoded.canApproveInvoice).toBe(false);
    expect(decoded.canPostInvoice).toBe(true);
    expect(decoded.canSendInvoice).toBe(true);
  });

  it('decodes a valid firm books payload', () => {
    const raw = {
      accounts: [
        {
          id: id1,
          code: '5000',
          name: 'Office Supplies',
          accountType: 'EXPENSE',
        },
      ],
      expenses: [
        {
          id: id2,
          expenseDate: '2026-10-01',
          category: 'OFFICE_SUPPLIES',
          payee: 'Office Depot',
          description: 'Stationery and printing supplies',
          amount: '750.00',
          currency: 'QAR',
          evidenceFileName: 'receipt_stationery.pdf',
          evidenceSha256: hash64,
          status: 'APPROVED',
          reviewComment: null,
          preparedByMe: false,
        },
      ],
      categories: ['OFFICE_SUPPLIES', 'TRAVEL', 'SOFTWARE'],
      canPrepare: true,
      canReview: true,
      maxEvidenceBytes: 10485760,
      maxReviewCommentLength: 1000,
    };

    const decoded = decodeBooks(raw, 'books');
    expect(decoded.accounts.length).toBe(1);
    expect(decoded.expenses[0].payee).toBe('Office Depot');
    expect(decoded.categories.length).toBe(3);
  });

  it('decodes a valid firm trial balance payload', () => {
    const raw = {
      fromPeriod: '2026-01',
      toPeriod: '2026-12',
      rows: [
        {
          accountId: id1,
          code: '1000',
          name: 'Operating Cash',
          accountType: 'ASSET',
          openingDebit: '100000.00',
          openingCredit: '0.00',
          movementDebit: '50000.00',
          movementCredit: '20000.00',
          closingDebit: '130000.00',
          closingCredit: '0.00',
        },
      ],
      totalDebit: '130000.00',
      totalCredit: '130000.00',
      balanced: true,
      revenue: '50000.00',
      expenses: '20000.00',
      profit: '30000.00',
      assets: '130000.00',
      liabilities: '0.00',
      equity: '130000.00',
      positionReconciles: true,
    };

    const decoded = decodeTrialBalance(raw, 'trialBalance');
    expect(decoded.balanced).toBe(true);
    expect(decoded.positionReconciles).toBe(true);
    expect(decoded.rows.length).toBe(1);
  });
});
