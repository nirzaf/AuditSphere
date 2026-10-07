import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/session';
import { ClientOperationalJournals, decodeOperationalJournal, decodeOperationalLedger, nativeJournalAmount, decodeJournalPreview, decodeJournalSnapshots, decodePostingReceipt, decodeJournalList } from './operational-journals';

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
    const ledger = { bookkeepingActive: true, clientId: client, periodId: client, periodCode: '2026', currency: 'QAR', basis: 'STATUTORY', page: 0, pageSize: 100, totalEntries: 2, periodTotalEntries: 2, postingSnapshotThrough: '9223372036854775807',
      trialBalance: { fromDate: '2026-01-01', toDate: '2026-12-31', source: 'NATIVE_POSTED_PERIOD_ACTIVITY', openingDebit: '0', openingCredit: '0', periodDebit: '125', periodCredit: '125', closingDebit: '125', closingCredit: '125', rows: [
        { accountId: account, accountCode: '1000', accountName: 'Cash', openingDebit: '0', openingCredit: '0', periodDebit: '0', periodCredit: '125', closingDebit: '0', closingCredit: '125' },
        { accountId: actor, accountCode: '6000', accountName: 'Expense', openingDebit: '0', openingCredit: '0', periodDebit: '125', periodCredit: '0', closingDebit: '125', closingCredit: '0' }] },
      accounts: [{ accountId: account, accountCode: '1000', accountName: 'Cash', debitMovement: '0.000000', creditMovement: '125.000000', netMovement: '-125.000000' }],
      entries: [{ journalId: journal, journalNumber: 'J-1', postingDate: '2026-01-05', lineNumber: 1, accountCode: '6000', accountName: 'Expense', description: 'Supplies', debit: '125.000000', credit: '0.000000' }] };
    expect(decodeOperationalLedger(ledger, client, client).entries).toHaveLength(1);
    expect(() => decodeOperationalLedger({ ...ledger, trialBalance: { ...ledger.trialBalance, closingDebit: '124' } }, client, client)).toThrow();
    expect(() => decodeOperationalLedger({ ...ledger, trialBalance: { ...ledger.trialBalance, rows: [{ ...ledger.trialBalance.rows[0], openingDebit: 0 }, ledger.trialBalance.rows[1]] } }, client, client)).toThrow();
    expect(() => decodeOperationalLedger({ ...ledger, trialBalance: { ...ledger.trialBalance, source: 'EXTERNAL' } }, client, client)).toThrow();
    expect(decodeOperationalLedger(ledger, client, client).accounts[0].netMovement).toBe('-125.000000');
    expect(() => decodeOperationalLedger({ ...ledger, periodId: journal }, client, client)).toThrow();
    expect(() => decodeOperationalLedger({ ...ledger, entries: [{ ...ledger.entries[0], credit: 0 }] }, client, client)).toThrow();
    expect(() => decodeOperationalLedger({ ...ledger, postingSnapshotThrough: 9223372036854775807n.toString() + '0' }, client, client)).toThrow();
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
  it('validates immutable source document and evidence identities for GL drill-through', () => {
    const saved = decodeOperationalJournal(view, client);
    const origin = { sourceKind: 'PURCHASE_INVOICE', sourceId: account, submissionId: actor, sourceRevision: '3',
      reference: 'SUP-2026-01', manifestSha256: 'a'.repeat(64), intentSha256: 'c'.repeat(64), evidenceId: journal, evidenceSha256: 'b'.repeat(64), evidenceReference: null };
    const snapshot = { journalId: journal, clientId: client, revision: '2', capturedAt: '2026-01-05T01:00:00Z',
      journalNumber: 'J-1', description: 'Historical content', postingDate: '2026-01-05', currency: 'QAR', lines: view.lines, sourceOrigins: [origin] };
    expect(decodeJournalSnapshots([snapshot], saved)[0].sourceOrigins?.[0].manifestSha256).toBe('a'.repeat(64));
    for (const change of [{ sourceKind: 'TAX_RETURN' }, { sourceId: 'bad' }, { submissionId: 'bad' },
      { sourceRevision: '0' }, { reference: '' }, { manifestSha256: 'bad' }, { intentSha256: 'bad' }, { evidenceId: 'bad' }, { evidenceSha256: 'bad' }])
      expect(() => decodeJournalSnapshots([{ ...snapshot, sourceOrigins: [{ ...origin, ...change }] }], saved)).toThrow();
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


describe('Saved native journals', () => {
  const listed = { clientId: client, periodId: null, status: null, page: 0, pageSize: 25, totalJournals: 1, bookkeepingActive: false,
    journals: [{ id: journal, periodId: client, journalNumber: 'J-1', description: 'Retained history', postingDate: '2026-01-15', currency: 'QAR', status: 'POSTED', revision: '3', createdByUserId: actor }] };
  it('retains scoped history while disclosing an inactive service', () => {
    expect(decodeJournalList(listed, client, null, null, 0).bookkeepingActive).toBe(false);
  });
  it('refuses changed scope, filters, pages, identities, revisions and service flags', () => {
    for (const changed of [{ ...listed, clientId: journal }, { ...listed, periodId: client }, { ...listed, status: 'DRAFT' }, { ...listed, page: 1 },
      { ...listed, bookkeepingActive: 'false' }, { ...listed, totalJournals: 0 }, { ...listed, journals: [listed.journals[0], listed.journals[0]] },
      { ...listed, journals: [{ ...listed.journals[0], revision: 3 }] }]) expect(() => decodeJournalList(changed, client, null, null, 0)).toThrow();
  });
});


describe('Invoice journal provenance', () => {
  const origin = { clientId: client, journalId: journal, invoiceId: account, submissionId: actor,
    draftRevision: '1', journalSubmittedRevision: '2' };
  const submitted = { ...view, status: 'SUBMITTED', revision: '2', invoiceOrigin: origin };
  it('allows 101 generated invoice lines only with scoped invoice and submission identities', () => {
    const lines = Array.from({ length: 101 }, (_, index) => ({ ...view.lines[index === 0 ? 0 : 1], lineNumber: index + 1 }));
    expect(decodeOperationalJournal({ ...submitted, lines }, client).invoiceOrigin?.invoiceId).toBe(account);
    expect(() => decodeOperationalJournal({ ...submitted, lines, invoiceOrigin: null }, client)).toThrow();
    expect(() => decodeOperationalJournal({ ...submitted, lines: [...lines, { ...lines[0], lineNumber: 102 }] }, client)).toThrow();
    for (const change of [{ clientId: journal }, { journalId: account }, { invoiceId: 'bad' }, { submissionId: 'bad' },
      { invoiceId: '00000000-0000-0000-0000-000000000000' }, { draftRevision: 1 }, { draftRevision: '0' },
      { draftRevision: '9223372036854775808' }, { journalSubmittedRevision: '1' }, { journalSubmittedRevision: '3' }])
      expect(() => decodeOperationalJournal({ ...submitted, lines, invoiceOrigin: { ...origin, ...change } }, client)).toThrow();
  });
  it('binds current and historical invoice origin to the exact submitted native revision', () => {
    const currentOrigin = { ...origin, draftRevision: '2', submissionId: client, journalSubmittedRevision: '5' };
    const current = decodeOperationalJournal({ ...submitted, revision: '5', invoiceOrigin: currentOrigin }, client);
    const snapshot = { journalId: journal, clientId: client, revision: '2', capturedAt: '2026-01-05T01:00:00Z',
      journalNumber: 'J-1', description: 'Prior invoice submission', postingDate: '2026-01-05', currency: 'QAR', lines: view.lines, invoiceOrigin: origin };
    expect(decodeJournalSnapshots([snapshot], current)[0].invoiceOrigin?.submissionId).toBe(actor);
    expect(() => decodeJournalSnapshots([{ ...snapshot, invoiceOrigin: currentOrigin }], current)).toThrow();
    expect(() => decodeOperationalJournal({ ...submitted, status: 'POSTED', revision: '4' }, client)).toThrow();
    expect(decodeOperationalJournal({ ...submitted, status: 'POSTED', revision: '3' }, client).invoiceOrigin?.submissionId).toBe(actor);
    const preview = { journalId: journal, clientId: client, periodId: client, revision: '2', status: 'SUBMITTED', currency: 'QAR',
      totalDebit: '125.000000', totalCredit: '125.000000', digest: 'a'.repeat(64), lines: view.lines };
    expect(() => decodeJournalPreview(preview, decodeOperationalJournal(submitted, client))).toThrow();
  });
  it('discloses invoice provenance in saved-work discovery and posting receipt reads', () => {
    const listed = { clientId: client, periodId: null, status: null, page: 0, pageSize: 25, totalJournals: 1, bookkeepingActive: true,
      journals: [{ id: journal, periodId: client, journalNumber: 'J-1', description: 'Invoice posting', postingDate: '2026-01-15',
        currency: 'QAR', status: 'POSTED', revision: '3', createdByUserId: actor, invoiceOrigin: origin }] };
    expect(decodeJournalList(listed, client, null, null, 0).journals[0].invoiceOrigin?.invoiceId).toBe(account);
    expect(() => decodeJournalList({ ...listed, journals: [{ ...listed.journals[0], invoiceOrigin: { ...origin, journalId: account } }] }, client, null, null, 0)).toThrow();
    const receipt = { commandId: account, clientId: client, journalId: journal, actorUserId: actor, submittedRevision: '2', postedRevision: '3',
      previewDigest: 'a'.repeat(64), intentHash: 'b'.repeat(64), recordedAt: '2026-01-05T01:00:00Z', status: 'POSTED', invoiceOrigin: origin };
    expect(decodePostingReceipt(receipt, client, account, actor).invoiceOrigin?.submissionId).toBe(actor);
    expect(() => decodePostingReceipt({ ...receipt, invoiceOrigin: { ...origin, journalSubmittedRevision: '5' } }, client, account, actor)).toThrow();
  });
});


describe('Invoice journals in the native journal workbench', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [ClientOperationalJournals], providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: actor, firmId: client, generation: '1', staff: true });
  });
  afterEach(() => { http.verify(); TestBed.resetTestingModule(); });
  it('retains invoice history and hides or refuses every generic journal action', () => {
    const fixture = TestBed.createComponent(ClientOperationalJournals);
    fixture.componentRef.setInput('clientId', client);
    fixture.componentRef.setInput('periods', [{ id: client, code: '2026', start: '2026-01-01', end: '2026-12-31', currency: 'QAR', status: 'OPEN' }]);
    fixture.componentRef.setInput('bookCurrency', 'QAR');
    fixture.detectChanges();
    const component = fixture.componentInstance;
    for (const [status, revision] of [['DRAFT', '4'], ['SUBMITTED', '2'], ['RETURNED', '3'], ['POSTED', '3']]) {
      const saved = decodeOperationalJournal({ ...view, status, revision,
        invoiceOrigin: { clientId: client, journalId: journal, invoiceId: account, submissionId: actor, draftRevision: '1', journalSubmittedRevision: '2' } }, client);
      component.journal.set(saved); fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('[aria-label="Client sales invoice origin"]')?.textContent).toContain(account);
      const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')).map(button => (button as HTMLButtonElement).textContent?.trim());
      expect(buttons).toContain('View submitted versions');
      for (const label of ['Preview accounting effect', 'Edit draft journal', 'Edit returned journal', 'Submit for independent review', 'Approve and post', 'Return for rework', 'Save reversal draft']) expect(buttons).not.toContain(label);
      component.reviewed.set(true); component.reason = 'Independent review';
      component.reversalPeriodId = client; component.reversalDate = '2026-01-06';
      component.editJournal(saved); component.loadPreview(saved); component.submit(saved); component.returnJournal(saved); component.approve(saved); component.createReversal(saved);
      expect(component.editing()).toBeNull();
    }
    http.expectNone(request => request.url.includes('/operational-journals/'));
  });

  it('carries the first report page high-water mark into the next ledger page', () => {
    const fixture = TestBed.createComponent(ClientOperationalJournals);
    fixture.componentRef.setInput('clientId', client);
    fixture.componentRef.setInput('periods', [{ id: client, code: '2026', start: '2026-01-01', end: '2026-12-31', currency: 'QAR', status: 'OPEN' }]);
    fixture.componentRef.setInput('bookCurrency', 'QAR');
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.periodId = client;
    component.loadLedger(0);
    const first = http.expectOne(request => request.url.endsWith('/operational-ledger') && request.params.get('page') === '0');
    const response = (page: number) => ({ bookkeepingActive: true, clientId: client, periodId: client, periodCode: '2026', currency: 'QAR',
      basis: 'STATUTORY', page, pageSize: 100, totalEntries: 101, periodTotalEntries: 150, postingSnapshotThrough: '9007199254740993',
      accounts: [], entries: [], trialBalance: { fromDate: '2026-01-01', toDate: '2026-12-31', source: 'NATIVE_POSTED_PERIOD_ACTIVITY',
        openingDebit: '0', openingCredit: '0', periodDebit: '0', periodCredit: '0', closingDebit: '0', closingCredit: '0', rows: [] } });
    first.flush(response(0));
    expect(component.ledger()?.postingSnapshotThrough).toBe('9007199254740993');
    component.loadLedger(1);
    const second = http.expectOne(request => request.url.endsWith('/operational-ledger') && request.params.get('page') === '1');
    expect(second.request.params.get('postingSnapshotThrough')).toBe('9007199254740993');
    second.flush(response(1));
    expect(component.ledger()?.page).toBe(1);
    expect(component.ledger()?.postingSnapshotThrough).toBe('9007199254740993');
  });

  it('sends ledger filters with the stable snapshot and resets to page zero', () => {
    const fixture = TestBed.createComponent(ClientOperationalJournals);
    fixture.componentRef.setInput('clientId', client);
    fixture.componentRef.setInput('periods', [{ id: client, code: '2026', start: '2026-01-01', end: '2026-12-31', currency: 'QAR', status: 'OPEN' }]);
    fixture.componentRef.setInput('bookCurrency', 'QAR');
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.ledgerPeriodId = client;
    component.ledgerFrom = '2026-01-01'; component.ledgerTo = '2026-06-30';
    component.ledgerAccountFrom = '4000'; component.ledgerAccountTo = '4999';
    component.ledgerSourceType = 'SALES_INVOICE'; component.ledgerReference = 'INV-'; component.ledgerCounterpartyId = account;
    component.loadLedger(0);
    const request = http.expectOne(r => r.url.endsWith('/operational-ledger'));
    expect(request.request.params.get('accountCodeFrom')).toBe('4000');
    expect(request.request.params.get('accountCodeTo')).toBe('4999');
    expect(request.request.params.get('sourceType')).toBe('SALES_INVOICE');
    expect(request.request.params.get('reference')).toBe('INV-');
    expect(request.request.params.get('counterpartyId')).toBe(account);
    request.flush({ bookkeepingActive: true, clientId: client, periodId: client, periodCode: '2026', currency: 'QAR',
      basis: 'STATUTORY', page: 0, pageSize: 100, totalEntries: 0, periodTotalEntries: 5, postingSnapshotThrough: '1',
      accounts: [], entries: [], trialBalance: { fromDate: '2026-01-01', toDate: '2026-06-30', source: 'NATIVE_POSTED_PERIOD_ACTIVITY',
        openingDebit: '0', openingCredit: '0', periodDebit: '0', periodCredit: '0', closingDebit: '0', closingCredit: '0', rows: [] } });
    expect(component.ledger()?.periodTotalEntries).toBe(5);
    expect(() => decodeOperationalLedger({ bookkeepingActive: true, clientId: client, periodId: client, periodCode: '2026', currency: 'QAR',
      basis: 'STATUTORY', page: 0, pageSize: 100, totalEntries: 1, postingSnapshotThrough: '1', accounts: [], entries: [], trialBalance: {} }, client, client)).toThrow();
  });
});
