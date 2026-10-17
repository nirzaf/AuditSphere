import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PurchaseCreditNoteWorkflow } from './purchase-credit-note-workflow';
import { SessionService } from '../../core/session';

const client = '11111111-1111-4111-8111-111111111111';
const actor = '22222222-2222-4222-8222-222222222222';
const otherActor = '33333333-3333-4333-8333-333333333333';
const creditNote = '44444444-4444-4444-8444-444444444444';
const submission = '55555555-5555-4555-8555-555555555555';
const command = '66666666-6666-4666-8666-666666666666';
const supplier = '77777777-7777-4777-8777-777777777777';
const period = '88888888-8888-4888-8888-888888888888';
const apRole = '99999999-9999-4999-8999-999999999999';
const base = `/api/ui/accounting/clients/${client}`;
const otherClient = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const supplier26 = '00000026-0000-4000-8000-000000000026';
const storageKey = `auditsphere.purchase-credit-command.v1:${client}`;
const digest = 'a'.repeat(64);
const supplierRow = (id: string, clientId = client) => ({ id, clientId, legalName: `Supplier ${id.slice(0, 8)}`, displayName: `Supplier ${id.slice(0, 8)}`, role: 'SUPPLIER', address: '', country: 'QA', taxIdentifier: '', contactDetails: '', paymentTerms: '', defaultCurrency: 'QAR', externalSystem: '', externalReference: '', createdByUserId: actor, createdAt: '2026-01-01T00:00:00Z', revision: '1', effectiveAmendmentId: null });
const supplierPage = (clientId: string, pageNumber: number, total: number, counterparties: ReturnType<typeof supplierRow>[]) => ({ clientId, role: 'SUPPLIER', page: pageNumber, pageSize: 25, total, bookkeepingActive: true, counterparties });

function receipt(kind: 'SUBMIT' | 'REVIEW', outcome: string, actorUserId = actor) {
  return { commandId: command, creditNoteId: creditNote, submissionId: submission, kind, actorUserId, intentHash: digest,
    outcome, decisionId: kind === 'REVIEW' ? 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' : null };
}

describe('Purchase credit command recovery', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [PurchaseCreditNoteWorkflow], providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: actor, firmId: client, generation: '1', staff: true });
  });
  afterEach(() => { try { http.verify({ ignoreCancelled: true }); } finally { TestBed.resetTestingModule(); sessionStorage.clear(); } });

  function create() {
    const fixture = TestBed.createComponent(PurchaseCreditNoteWorkflow);
    fixture.componentRef.setInput('clientId', client);
    fixture.componentRef.setInput('currency', 'QAR');
    fixture.componentRef.setInput('periods', [{ id: period, code: '2026', status: 'OPEN' }]);
    fixture.detectChanges();
    fixture.componentInstance.refresh();
    const reads = http.match(`${base}/purchase-credit-notes`).filter(request => !request.cancelled);
    expect(reads).toHaveLength(1);
    reads[0].flush([]);
    return fixture;
  }

  it('keeps an unknown submit fenced after an empty lookup and retries only the original request', () => {
    const fixture = create();
    const c = fixture.componentInstance;
    c.supplierId = supplier; c.periodId = period; c.payableRoleId = apRole; c.reference = 'CN-001';
    c.reason = 'Supplier credit'; c.sourceBasis = 'Supplier document CN-001';
    c.lines = [{ originalLineNumber: '', accountCode: '6000', amount: '12.50' }];
    c.preview.set({ creditNoteId: creditNote, originalInvoiceId: null, currency: 'QAR', totalCredit: '12.500000', digest,
      originalAmount: '0.000000', previouslyCredited: '0.000000', remainingCreditLimit: '0.000000', duplicateWarnings: [], manifest: { lines: [] } });
    c.submitAssent = true;
    vi.spyOn(globalThis.crypto, 'randomUUID').mockReturnValueOnce(creditNote).mockReturnValueOnce(command);

    c.submitCredit();
    const submit = http.expectOne(`${base}/purchase-credit-notes`);
    const exactBody = structuredClone(submit.request.body);
    expect(exactBody.commandId).toMatch(/^[0-9a-f-]{36}$/i);
    expect(exactBody.credit.creditNoteId).toBe(creditNote);
    expect(JSON.parse(sessionStorage.getItem(storageKey)!).body).toEqual(exactBody);
    submit.error(new ProgressEvent('network failure'));
    expect(c.pendingCommand()?.commandId).toBe(exactBody.commandId);

    c.checkPendingOutcome();
    http.expectOne(`${base}/purchase-credit-note-requests/${exactBody.commandId}?kind=SUBMIT`).flush({ code: 'command.receipt-not-found' }, { status: 404, statusText: 'Not Found' });
    expect(c.pendingCommand()?.commandId).toBe(exactBody.commandId);
    expect(sessionStorage.getItem(storageKey)).not.toBeNull();

    c.reference = 'CHANGED-INTENT'; c.preview.set({ ...c.preview()!, digest: 'b'.repeat(64) });
    c.submitCredit();
    http.expectNone(`${base}/purchase-credit-notes`);
    c.retryPendingCommand();
    const retry = http.expectOne(`${base}/purchase-credit-notes`);
    expect(retry.request.body).toEqual(exactBody);
    retry.flush(receipt('SUBMIT', 'SUBMITTED'));
    http.expectOne(`${base}/purchase-credit-notes`).flush([]);
    expect(c.pendingCommand()).toBeNull();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
    fixture.destroy();
  });

  it('restores an unknown return, checks a matching actor-owned receipt, and requires acknowledgement', () => {
    let fixture = create();
    const c = fixture.componentInstance;
    const requestBody = { commandId: command, submissionId: submission, decision: 'RETURN', reason: 'Correct the account code',
      duplicateResolutionReason: '', previewDigest: digest, reviewed: true };
    c.activeReview.set({ submissionId: submission, digest, canPost: true, postingBlock: null, duplicateWarning: false, manifest: { lines: [] } });
    c.reviewReason = requestBody.reason;
    const row = { creditNoteId: creditNote, creditNoteReference: 'CN-002', originalInvoiceId: null, submissionId: submission,
      state: 'SUBMITTED', currency: 'QAR', amount: '12.500000', postingDate: '2026-01-01', reason: 'Supplier credit', makerId: otherActor,
      decision: null, decisionReason: null, duplicateResolutionReason: null, unappliedSupplierDebit: false };
    vi.spyOn(globalThis.crypto, 'randomUUID').mockReturnValue(command);
    c.review(row, 'RETURN');
    const returned = http.expectOne(`${base}/purchase-credit-note-reviews`);
    expect(returned.request.body).toEqual(requestBody);
    returned.error(new ProgressEvent('network failure'));
    expect(c.pendingCommand()?.kind).toBe('REVIEW');
    fixture.destroy();

    fixture = create();
    const restored = fixture.componentInstance;
    expect(restored.pendingCommand()?.body).toEqual(requestBody);
    restored.checkPendingOutcome();
    http.expectOne(`${base}/purchase-credit-note-requests/${command}?kind=REVIEW`).flush(receipt('REVIEW', 'RETURNED'));
    expect(restored.pendingCommand()?.commandId).toBe(command);
    expect(restored.recoveredReceipt()?.outcome).toBe('RETURNED');
    expect(sessionStorage.getItem(storageKey)).not.toBeNull();

    restored.acknowledgePendingOutcome();
    http.expectOne(`${base}/purchase-credit-notes`).flush([]);
    expect(restored.pendingCommand()).toBeNull();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
    fixture.destroy();
  });

  it('does not accept a receipt owned by a different actor', () => {
    const pending = { version: 1, clientId: client, actorId: actor, kind: 'SUBMIT', commandId: command,
      body: { commandId: command, credit: { creditNoteId: creditNote }, previewDigest: digest, reviewed: true } };
    sessionStorage.setItem(storageKey, JSON.stringify(pending));
    const fixture = create();
    const c = fixture.componentInstance;
    expect(c.pendingCommand()?.commandId).toBe(command);
    c.checkPendingOutcome();
    http.expectOne(`${base}/purchase-credit-note-requests/${command}?kind=SUBMIT`).flush(receipt('SUBMIT', 'SUBMITTED', otherActor));
    expect(c.pendingCommand()?.commandId).toBe(command);
    expect(c.recoveredReceipt()).toBeNull();
    expect(sessionStorage.getItem(storageKey)).not.toBeNull();
    fixture.destroy();
  });

  it('selects supplier 26 on the next page and preserves it when changing supplier pages', () => {
    const fixture = create(), c = fixture.componentInstance;
    const firstPage = Array.from({ length: 25 }, (_, index) => supplierRow(`000000${String(index + 1).padStart(2, '0')}-0000-4000-8000-${String(index + 1).padStart(12, '0')}`));
    c.loadSuppliers(0);
    http.expectOne(`${base}/counterparties?role=SUPPLIER&page=0&pageSize=25`).flush(supplierPage(client, 0, 26, firstPage));
    expect(c.supplierTotal()).toBe(26);
    c.loadSuppliers(1);
    http.expectOne(`${base}/counterparties?role=SUPPLIER&page=1&pageSize=25`).flush(supplierPage(client, 1, 26, [supplierRow(supplier26)]));
    c.selectSupplier(supplier26);
    c.preview.set({ creditNoteId: creditNote, originalInvoiceId: null, currency: 'QAR', totalCredit: '12.500000', digest, originalAmount: '0.000000', previouslyCredited: '0.000000', remainingCreditLimit: '0.000000', duplicateWarnings: [], manifest: { lines: [] } });
    c.submitAssent = true;
    c.loadSuppliers(0);
    http.expectOne(`${base}/counterparties?role=SUPPLIER&page=0&pageSize=25`).flush(supplierPage(client, 0, 26, firstPage));
    expect(c.supplierId).toBe(supplier26);
    expect(c.supplierOptions().some(item => item.id === supplier26)).toBe(true);
    expect(c.preview()).not.toBeNull(); expect(c.submitAssent).toBe(true);
    c.selectSupplier(firstPage[0].id);
    expect(c.preview()).toBeNull(); expect(c.submitAssent).toBe(false); expect(c.originalInvoiceId).toBe('');
    fixture.destroy();
  });

  it('clears supplier selection and preview on client change and cancels the old client page read', () => {
    const fixture = create(), c = fixture.componentInstance;
    c.loadSuppliers(0);
    const oldPage = http.expectOne(`${base}/counterparties?role=SUPPLIER&page=0&pageSize=25`);
    c.selectSupplier(supplier);
    c.preview.set({ creditNoteId: creditNote, originalInvoiceId: null, currency: 'QAR', totalCredit: '12.500000', digest, originalAmount: '0.000000', previouslyCredited: '0.000000', remainingCreditLimit: '0.000000', duplicateWarnings: [], manifest: { lines: [] } });
    c.submitAssent = true;
    fixture.componentRef.setInput('clientId', otherClient); fixture.detectChanges();
    expect(oldPage.cancelled).toBe(true);
    expect(c.supplierId).toBe(''); expect(c.selectedSupplier()).toBeNull(); expect(c.preview()).toBeNull(); expect(c.submitAssent).toBe(false);
    http.expectOne(`/api/ui/accounting/clients/${otherClient}/purchase-credit-notes`).flush([]);
    expect(c.supplierPage()).toBe(0); expect(c.supplierTotal()).toBe(0);
    fixture.destroy();
  });

  it('keeps only the newest requested supplier page after a stale read is superseded', () => {
    const fixture = create(), c = fixture.componentInstance;
    c.loadSuppliers(0);
    http.expectOne(`${base}/counterparties?role=SUPPLIER&page=0&pageSize=25`).flush(supplierPage(client, 0, 26, [supplierRow(supplier)]));
    c.loadSuppliers(1);
    const obsolete = http.expectOne(`${base}/counterparties?role=SUPPLIER&page=1&pageSize=25`);
    c.loadSuppliers(0);
    const current = http.expectOne(`${base}/counterparties?role=SUPPLIER&page=0&pageSize=25`);
    expect(obsolete.cancelled).toBe(true);
    current.flush(supplierPage(client, 0, 1, [supplierRow(supplier)]));
    expect(c.supplierPage()).toBe(0); expect(c.supplierTotal()).toBe(1); expect(c.suppliers().map(item => item.id)).toEqual([supplier]);
    fixture.destroy();
  });
});
