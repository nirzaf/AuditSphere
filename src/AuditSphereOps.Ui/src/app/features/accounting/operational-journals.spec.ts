import { describe, expect, it } from 'vitest';
import { decodeOperationalJournal, decodeOperationalLedger } from './operational-journals';

const client = '11111111-1111-4111-8111-111111111111';
const journal = '22222222-2222-4222-8222-222222222222';
const account = '33333333-3333-4333-8333-333333333333';
const actor = '44444444-4444-4444-8444-444444444444';
const view = { id: journal, clientId: client, periodId: client, journalNumber: 'J-1', description: 'Office supplies', postingDate: '2026-01-05',
  currency: 'QAR', status: 'DRAFT', revision: '1', createdByUserId: actor, createdAt: '2026-01-05T00:00:00Z', postedByUserId: null, postedAt: null,
  lines: [{ lineNumber: 1, accountId: account, accountCode: '6000', accountName: 'Expense', description: 'Supplies', debit: '125.000000', credit: '0.000000' },
    { lineNumber: 2, accountId: account, accountCode: '1000', accountName: 'Cash', description: 'Cash', debit: '0.000000', credit: '125.000000' }] };

describe('Client operational journal transport contract', () => {
  it('accepts a client-scoped journal with exact decimal strings', () => {
    expect(decodeOperationalJournal(view, client).lines[0].debit).toBe('125.000000');
  });
  it('rejects cross-client records, numeric money, invalid account identities and unbounded lines', () => {
    expect(() => decodeOperationalJournal({ ...view, clientId: journal }, client)).toThrow();
    expect(() => decodeOperationalJournal({ ...view, lines: [{ ...view.lines[0], debit: 125 }, view.lines[1]] }, client)).toThrow();
    expect(() => decodeOperationalJournal({ ...view, lines: [{ ...view.lines[0], accountId: 'bad' }, view.lines[1]] }, client)).toThrow();
    expect(() => decodeOperationalJournal({ ...view, lines: Array.from({ length: 101 }, () => view.lines[0]) }, client)).toThrow();
  });
  it('binds posted movement pages to the exact client and period using decimal strings', () => {
    const ledger = { clientId: client, periodId: client, periodCode: '2026', currency: 'QAR', basis: 'STATUTORY', page: 0, pageSize: 100, totalEntries: 2,
      accounts: [{ accountId: account, accountCode: '1000', accountName: 'Cash', debitMovement: '0.000000', creditMovement: '125.000000', netMovement: '-125.000000' }],
      entries: [{ journalId: journal, journalNumber: 'J-1', postingDate: '2026-01-05', lineNumber: 1, accountCode: '6000', accountName: 'Expense', description: 'Supplies', debit: '125.000000', credit: '0.000000' }] };
    expect(decodeOperationalLedger(ledger, client, client).entries).toHaveLength(1);
    expect(decodeOperationalLedger(ledger, client, client).accounts[0].netMovement).toBe('-125.000000');
    expect(() => decodeOperationalLedger({ ...ledger, periodId: journal }, client, client)).toThrow();
    expect(() => decodeOperationalLedger({ ...ledger, entries: [{ ...ledger.entries[0], credit: 0 }] }, client, client)).toThrow();
  });
});
