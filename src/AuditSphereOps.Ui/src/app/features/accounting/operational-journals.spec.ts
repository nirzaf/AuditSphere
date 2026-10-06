import { describe, expect, it } from 'vitest';
import { decodeOperationalJournal, decodeOperationalLedger, nativeJournalAmount, decodeJournalPreview, decodeJournalSnapshots, decodePostingReceipt } from './operational-journals';

const client = '11111111-1111-4111-8111-111111111111';
const journal = '22222222-2222-4222-8222-222222222222';
const account = '33333333-3333-4333-8333-333333333333';
const actor = '44444444-4444-4444-8444-444444444444';
const view = { id: journal, clientId: client, periodId: client, journalNumber: 'J-1', description: 'Office supplies', postingDate: '2026-01-05',
  decisions: [], currency: 'QAR', status: 'DRAFT', revision: '1', createdByUserId: actor, createdAt: '2026-01-05T00:00:00Z', postedByUserId: null, postedAt: null,
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

describe('Native journal storage amount limits', () => {
  it('allows exact storage-scale values and refuses overflow and rounding', () => {
    expect(nativeJournalAmount('9999999999999.999999')).toBe(true);
    expect(nativeJournalAmount('0.000001')).toBe(true);
    for (const amount of ['10000000000000', '0.0000001', '-1', '1e2', 'NaN']) expect(nativeJournalAmount(amount)).toBe(false);
  });
});

describe('Server journal preview binding', () => {
  it('requires the exact saved client, journal, revision, currency and line intent', () => {
    const saved = decodeOperationalJournal(view, client);
    const preview = { journalId: journal, clientId: client, periodId: client, revision: '1', status: 'DRAFT', currency: 'QAR',
      totalDebit: '125.000000', totalCredit: '125.000000', digest: 'a'.repeat(64), lines: view.lines };
    expect(decodeJournalPreview(preview, saved).totalDebit).toBe('125.000000');
    for (const change of [{ clientId: journal }, { journalId: client }, { revision: '2' }, { currency: 'USD' },
      { digest: 'bad' }, { totalDebit: 125 }, { totalCredit: '126.000000' },
      { lines: [{ ...view.lines[0], description: 'Changed intent' }, view.lines[1]] }])
      expect(() => decodeJournalPreview({ ...preview, ...change }, saved)).toThrow();
  });
});

describe('Journal review history', () => {
  it('keeps a scoped reviewer reason and revision while rejecting malformed history', () => {
    const decision = { revision: '2', decision: 'RETURN', reason: 'Explain the supporting expense', actorUserId: actor, createdAt: '2026-01-05T01:00:00Z' };
    expect(decodeOperationalJournal({ ...view, decisions: [decision] }, client).decisions[0].reason).toBe(decision.reason);
    for (const change of [{ revision: '0' }, { decision: 'EDIT' }, { reason: '' }, { actorUserId: 'bad' }])
      expect(() => decodeOperationalJournal({ ...view, decisions: [{ ...decision, ...change }] }, client)).toThrow();
  });
});

describe('Preserved submitted versions', () => {
  it('requires authorized journal identity, distinct revisions and exact string amounts', () => {
    const saved = decodeOperationalJournal(view, client);
    const snapshot = { journalId: journal, clientId: client, revision: '2', capturedAt: '2026-01-05T01:00:00Z',
      journalNumber: 'J-1', description: 'Historical content', postingDate: '2026-01-05', currency: 'QAR', lines: view.lines };
    expect(decodeJournalSnapshots([snapshot], saved)[0].description).toBe('Historical content');
    expect(() => decodeJournalSnapshots([{ ...snapshot, clientId: journal }], saved)).toThrow();
    expect(() => decodeJournalSnapshots([snapshot, snapshot], saved)).toThrow();
    expect(() => decodeJournalSnapshots([{ ...snapshot, lines: [{ ...view.lines[0], debit: 125 }, view.lines[1]] }], saved)).toThrow();
  });
});

describe('Posting receipt recovery', () => {
  it('binds the committed outcome to command, client, actor and consecutive exact revisions', () => {
    const receipt = { commandId: account, clientId: client, journalId: journal, actorUserId: actor, submittedRevision: '9007199254740993',
      postedRevision: '9007199254740994', previewDigest: 'a'.repeat(64), intentHash: 'b'.repeat(64), recordedAt: '2026-01-05T01:00:00Z', status: 'POSTED' };
    expect(decodePostingReceipt(receipt, client, account, actor).postedRevision).toBe('9007199254740994');
    for (const change of [{ commandId: client }, { clientId: journal }, { actorUserId: account }, { journalId: 'bad' },
      { submittedRevision: 2 }, { postedRevision: '9007199254740995' }, { previewDigest: 'bad' }, { intentHash: 'bad' }, { status: 'PENDING' }])
      expect(() => decodePostingReceipt({ ...receipt, ...change }, client, account, actor)).toThrow();
  });
});

describe('Full reversal lineage', () => {
  it('requires exact journal direction and preserved reason, evidence and preparer identities', () => {
    const link = { originalJournalId: account, reversalJournalId: journal, originalRevision: '6', reason: 'Duplicate expense',
      evidenceReference: 'SYN-EVIDENCE-1', preparedByUserId: actor, preparedAt: '2026-01-06T00:00:00Z', reversalStatus: 'DRAFT' };
    expect(decodeOperationalJournal({ ...view, reversalOf: link }, client).reversalOf?.evidenceReference).toBe('SYN-EVIDENCE-1');
    for (const change of [{ reversalJournalId: account }, { originalJournalId: 'bad' }, { reason: '' }, { evidenceReference: '' }, { preparedByUserId: 'bad' }, { originalRevision: 6 }])
      expect(() => decodeOperationalJournal({ ...view, reversalOf: { ...link, ...change } }, client)).toThrow();
    expect(() => decodeOperationalJournal({ ...view, reversedBy: link }, client)).toThrow();
  });
});
