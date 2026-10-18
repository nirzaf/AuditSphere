import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { PurchaseInvoiceWorkflow } from './purchase-invoice-workflow';
import { SessionService } from '../../core/session';

const client = '11111111-1111-4111-8111-111111111111';
const invoice = '22222222-2222-4222-8222-222222222222';
const supplier = '33333333-3333-4333-8333-333333333333';
const actor = '44444444-4444-4444-8444-444444444444';
const period = '55555555-5555-4555-8555-555555555555';
const account = '66666666-6666-4666-8666-666666666666';
const receipt = '77777777-7777-4777-8777-777777777777';
const submission = '88888888-8888-4888-8888-888888888888';
const base = `/api/ui/accounting/clients/${client}`;
const otherClient = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const supplier26 = '00000026-0000-4000-8000-000000000026';

const supplierRow = {
  id: supplier, clientId: client, legalName: 'Supplier Ltd', displayName: 'Supplier Ltd', role: 'SUPPLIER', address: '', country: 'QA',
  taxIdentifier: '', contactDetails: '', paymentTerms: '', defaultCurrency: 'QAR', externalSystem: '', externalReference: '',
  createdByUserId: actor, createdAt: '2026-01-01T00:00:00Z', revision: '1', effectiveAmendmentId: null
};

function savedDraft(revision: string, evidenceReference: string) {
  return {
    id: revision === '1' ? invoice : '99999999-9999-4999-8999-999999999999', invoiceId: invoice, clientId: client, periodId: period,
    supplierId: supplier, voucherReference: 'PV-001', supplierInvoiceReference: 'SUP-001', revision,
    snapshotHash: 'a'.repeat(64), createdByUserId: actor, state: 'DRAFT',
    snapshot: {
      version: 'client-purchase-invoice-v1', profileId: account, chartVersionId: account, supplierId: supplier, supplierName: 'Supplier Ltd',
      voucherReference: 'PV-001', supplierInvoiceReference: 'SUP-001', normalizedSupplierReference: 'SUP001', currency: 'QAR',
      receiptDate: '2026-01-20', documentDate: '2026-01-10', accountingDate: '2026-01-20', supplyDate: '2026-01-10', dueDate: '2026-02-10',
      policy: { currency: 'QAR', decimalPlaces: 2, midpointRule: 'AWAY_FROM_ZERO', residualTreatment: 'REJECT', maximumResidualMinorUnits: 0, roundingAccountCode: '' },
      lines: [{ lineNumber: 1, accountId: account, accountCode: '6000', accountName: 'Office expense', description: 'Supplies', quantity: '1', unitPrice: '100', discount: '0', net: '100.000000', tax: '0.000000', gross: '100.000000' }],
      net: '100.000000', tax: '0.000000', gross: '100.000000', statedNet: '100.000000', statedTax: '0.000000', statedGross: '100.000000',
      lateArrival: true, possibleDuplicateCount: 0, sourceReceiptId: receipt, sourceReceiptHash: 'b'.repeat(64), evidenceReference
    }
  };
}

function page(state: 'DRAFT' | 'RETURNED', revision: string) {
  return { clientId: client, page: 0, pageSize: 25, hasMore: false, rows: [{ invoiceId: invoice, periodId: period, supplierId: supplier,
    supplierName: 'Supplier Ltd', voucherReference: 'PV-001', supplierInvoiceReference: 'SUP-001', revision, state, makerId: actor }] };
}
function supplierRecord(id: string, clientId = client) { return { ...supplierRow, id, clientId, displayName: `Supplier ${id.slice(0, 8)}` }; }
function supplierList(clientId: string, pageNumber: number, total: number, rows: ReturnType<typeof supplierRecord>[]) {
  return { clientId, role: 'SUPPLIER', page: pageNumber, pageSize: 25, total, bookkeepingActive: true, counterparties: rows };
}

describe('Client purchase draft lifecycle', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [PurchaseInvoiceWorkflow], providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: actor, firmId: client, generation: '1', staff: true });
  });
  afterEach(() => { try { http.verify({ ignoreCancelled: true }); } finally { TestBed.resetTestingModule(); sessionStorage.clear(); } });

  function open(total = 1, supplierRows = [supplierRow]) {
    const fixture = TestBed.createComponent(PurchaseInvoiceWorkflow);
    fixture.componentRef.setInput('clientId', client);
    fixture.componentRef.setInput('currency', 'QAR');
    fixture.componentRef.setInput('periods', [{ id: period, code: '2026', status: 'OPEN' }]);
    fixture.detectChanges();
    http.expectOne(`${base}/counterparties?role=SUPPLIER&page=0&pageSize=25`).flush(supplierList(client, 0, total, supplierRows));
    http.expectOne(`${base}/purchase-invoice-drafts?page=0&pageSize=25`).flush({ clientId: client, page: 0, pageSize: 25, hasMore: false, rows: [] });
    http.expectOne(`${base}/purchase-invoices`).flush([]);
    return fixture;
  }
  function flushHistoryReads() {
    for (const request of http.match((r) => r.url === `${base}/purchase-invoice-drafts`))
      request.flush({ clientId: client, page: 0, pageSize: 25, hasMore: false, rows: [] });
    for (const request of http.match((r) => r.url === `${base}/purchase-invoices`)) request.flush([]);
  }

  it('resumes an unsubmitted draft and revises a returned draft under the same invoice identity and next revision', () => {
    const fixture = TestBed.createComponent(PurchaseInvoiceWorkflow);
    fixture.componentRef.setInput('clientId', client);
    fixture.componentRef.setInput('currency', 'QAR');
    fixture.componentRef.setInput('periods', [{ id: period, code: '2026', status: 'OPEN' }]);
    fixture.detectChanges();
    const c = fixture.componentInstance;

    http.expectOne(`${base}/counterparties?role=SUPPLIER&page=0&pageSize=25`).flush({ clientId: client, role: 'SUPPLIER', page: 0, pageSize: 25, total: 1, bookkeepingActive: true, counterparties: [supplierRow] });
    http.expectOne(`${base}/purchase-invoice-drafts?page=0&pageSize=25`).flush(page('DRAFT', '1'));
    http.expectOne(`${base}/purchase-invoices`).flush([]);

    c.resumeDraft(page('DRAFT', '1').rows[0]);
    http.expectOne(`${base}/purchase-invoice-drafts/${invoice}`).flush(savedDraft('1', 'Original supplier evidence'));
    http.expectOne(`${base}/purchase-invoice-drafts?page=0&pageSize=25`).flush(page('DRAFT', '1'));
    expect(c.editing()).toBe(true);
    expect(c.draft()?.invoiceId).toBe(invoice);
    expect(c.draft()?.periodId).toBe(period);
    expect(c.draft()?.snapshot.supplierName).toBe('Supplier Ltd');
    expect(c.draft()?.snapshot.gross).toBe('100.000000');
    expect(c.evidence).toBe('Original supplier evidence');
    expect(c.sourceReceiptId).toBe(receipt);
    expect(c.lines[0].quantity).toBe('1');

    c.draftAssent.set(true);
    c.submitAssent.set(true);
    c.draftChanged();
    expect(c.draftAssent()).toBe(false);
    expect(c.submitAssent()).toBe(false);
    c.evidence = 'Corrected supplier evidence';
    c.apRoleId = account;
    c.draftAssent.set(true);
    c.saveDraft();
    const revise = http.expectOne(`${base}/purchase-invoice-drafts`);
    expect(revise.request.body.invoiceId).toBe(invoice);
    expect(revise.request.body.expectedRevision).toBe('1');
    expect(revise.request.body.sourceReceiptId).toBe(receipt);
    expect(revise.request.body.evidenceReference).toBe('Corrected supplier evidence');
    const exactRequest = revise.request.body;
    revise.error(new ProgressEvent('network failure'));
    expect(c.pendingDraftSave()).not.toBeNull();
    c.retryDraftSave();
    const retry = http.expectOne(`${base}/purchase-invoice-drafts`);
    expect(retry.request.body).toEqual(exactRequest);
    retry.flush(savedDraft('2', 'Corrected supplier evidence'));
    http.expectOne(`${base}/purchase-invoice-drafts?page=0&pageSize=25`).flush(page('DRAFT', '2'));
    http.expectOne(`${base}/purchase-invoices`).flush([]);
    expect(c.draft()?.invoiceId).toBe(invoice);
    expect(c.draft()?.revision).toBe('2');
    expect(c.editing()).toBe(false);

    const returned = { invoiceId: invoice, supplierId: supplier, supplierInvoiceReference: 'SUP-001', voucherReference: 'PV-001',
      state: 'RETURNED', currency: 'QAR', gross: '100.000000', dueDate: '2026-02-10', receiptDate: '2026-01-20', lateArrival: true,
      duplicateWarning: false, submissionId: submission, journalId: null, makerId: actor, decision: 'RETURN',
      decisionReason: 'Use the approved office expense account.', duplicateResolutionReason: null, openItemCreated: false } as const;
    c.reworkReturnedDraft({ ...returned, makerId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' });
    http.expectNone(`${base}/purchase-invoice-drafts/${invoice}`);
    c.reworkReturnedDraft(returned);
    http.expectOne(`${base}/purchase-invoice-drafts/${invoice}`).flush(savedDraft('2', 'Corrected supplier evidence'));
    http.expectOne(`${base}/purchase-invoice-drafts?page=0&pageSize=25`).flush(page('RETURNED', '2'));
    expect(c.returnReason()).toBe('Use the approved office expense account.');
    expect(c.evidence).toBe('Corrected supplier evidence');
    expect(c.sourceReceiptId).toBe(receipt);

    c.evidence = 'Reworked evidence';
    c.draftChanged();
    c.apRoleId = account;
    c.draftAssent.set(true);
    c.saveDraft();
    const rework = http.expectOne(`${base}/purchase-invoice-drafts`);
    expect(rework.request.body.invoiceId).toBe(invoice);
    expect(rework.request.body.expectedRevision).toBe('2');
    expect(rework.request.body.sourceReceiptId).toBe(receipt);
    rework.flush(savedDraft('3', 'Reworked evidence'));
    http.expectOne(`${base}/purchase-invoice-drafts?page=0&pageSize=25`).flush(page('DRAFT', '3'));
    http.expectOne(`${base}/purchase-invoices`).flush([]);
    expect(c.draft()?.invoiceId).toBe(invoice);
    expect(c.draft()?.revision).toBe('3');
  });

  it('recovers a lost initial draft response after component navigation and retries the exact saved body', () => {
    const first = open();
    const c = first.componentInstance;
    c.periodId = period; c.supplierId = supplier; c.voucher = 'PV-RECOVER'; c.supplierReference = 'SUP-RECOVER'; c.apRoleId = account;
    c.receiptDate = '2026-01-20'; c.documentDate = '2026-01-10'; c.accountingDate = '2026-01-20'; c.supplyDate = '2026-01-10'; c.dueDate = '2026-02-10';
    c.evidence = 'Synthetic recovery evidence'; c.statedNet = '100'; c.statedTax = '0'; c.statedGross = '100'; c.lines = [{ accountCode: '6000', description: 'Supplies', quantity: '1', unitPrice: '100', discount: '0' }];
    c.draftAssent.set(true); c.saveDraft();
    const save = http.expectOne(`${base}/purchase-invoice-drafts`), exactBody = save.request.body;
    save.error(new ProgressEvent('network failure'));
    expect(c.pendingDraftSave()).not.toBeNull();
    c.loadSuppliers(0);
    http.expectOne(`${base}/counterparties?role=SUPPLIER&page=0&pageSize=25`).flush({ ...supplierList(client, 0, 1, [supplierRow]), bookkeepingActive: false });
    expect(c.pendingDraftSave()?.body).toEqual(exactBody); expect(c.hasPendingCommand()).toBe(true);
    first.destroy();

    const recovered = open(), next = recovered.componentInstance;
    expect(next.pendingDraftSave()?.body).toEqual(exactBody);
    expect(next.hasPendingCommand()).toBe(true);
    next.reconcilePendingOutcome();
    http.expectOne(`${base}/purchase-invoice-requests/${exactBody.commandId}?kind=DRAFT`).flush({ code: 'command.receipt-not-found' }, { status: 404, statusText: 'Not Found' });
    expect(next.hasPendingCommand()).toBe(true);
    next.retryPendingOutcome();
    const retry = http.expectOne(`${base}/purchase-invoice-drafts`);
    expect(retry.request.body).toEqual(exactBody);
    retry.flush(savedDraft('1', 'Synthetic recovery evidence'));
    http.expectOne(`${base}/purchase-invoice-requests/${exactBody.commandId}?kind=DRAFT`).flush({ commandId: exactBody.commandId, kind: 'DRAFT', actorUserId: actor,
      intentHash: 'c'.repeat(64), outcome: 'DRAFT', invoiceId: invoice, draftId: invoice, submissionId: null, decisionId: null,
      journalId: null, draft: savedDraft('1', 'Synthetic recovery evidence'), receipt: null, postingReceipt: null });
    expect(next.hasPendingCommand()).toBe(false);
    expect(next.draft()?.invoiceId).toBe(invoice);
    expect(next.draft()?.revision).toBe('1');
    flushHistoryReads();
  });

  it('retains the selected AP role for an immediate preview and requires it again for a reopened draft', () => {
    const fixture = open(), c = fixture.componentInstance;
    c.selectSupplier(supplier); c.periodId = period; c.voucher = 'PV-ROLE'; c.supplierReference = 'SUP-ROLE'; c.apRoleId = account;
    c.receiptDate = '2026-01-20'; c.documentDate = '2026-01-10'; c.accountingDate = '2026-01-20'; c.supplyDate = '2026-01-10'; c.dueDate = '2026-02-10';
    c.evidence = 'Synthetic AP role evidence'; c.statedNet = '100'; c.statedTax = '0'; c.statedGross = '100';
    c.lines = [{ accountCode: '6000', description: 'Supplies', quantity: '1', unitPrice: '100', discount: '0' }];
    c.draftAssent.set(true); c.saveDraft();
    const save = http.expectOne(`${base}/purchase-invoice-drafts`);
    expect(save.request.body.payableRoleId).toBeUndefined();
    save.flush(savedDraft('1', 'Synthetic AP role evidence'));
    expect(c.apRoleId).toBe(account);
    flushHistoryReads();
    fixture.detectChanges();
    const previewButton = () => [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(button => button.textContent?.trim() === 'Preview AP posting')!;
    expect(previewButton().disabled).toBe(false);

    c.resumeDraft(page('DRAFT', '1').rows[0]);
    http.expectOne(`${base}/purchase-invoice-drafts/${invoice}`).flush(savedDraft('1', 'Synthetic AP role evidence'));
    http.expectOne(`${base}/purchase-invoice-drafts?page=0&pageSize=25`).flush(page('DRAFT', '1'));
    expect(c.apRoleId).toBe('');
    c.cancelEdit(); fixture.detectChanges();
    expect(previewButton().disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Choose the approved payable control role before requesting a posting preview.');
    fixture.destroy();
  });

  it('retains the submit command identity and acknowledges only its exact recovered submission', () => {
    const fixture = open(), c = fixture.componentInstance;
    c.draft.set(savedDraft('1', 'Supplier evidence') as never);
    c.preview.set({ invoiceId: invoice, revision: '1', currency: 'QAR', net: '100.000000', tax: '0.000000', gross: '100.000000', digest: 'd'.repeat(64), lateArrival: false, duplicateWarnings: [], manifest: { lines: [] } });
    (c as unknown as { previewRequest: Record<string, unknown> | null }).previewRequest = { commandId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', expectedDraftRevision: '1', payableRoleId: account, previewDigest: '' };
    c.submitAssent.set(true); c.submit();
    const send = http.expectOne(`${base}/purchase-invoices/${invoice}/submit`), exactBody = send.request.body;
    expect(c.pendingSubmit()?.body['commandId']).toBe(exactBody.commandId);
    send.error(new ProgressEvent('network failure'));
    c.reconcilePendingOutcome();
    http.expectOne(`${base}/purchase-invoice-requests/${exactBody.commandId}?kind=SUBMIT`).flush({ commandId: exactBody.commandId, kind: 'SUBMIT', actorUserId: actor,
      intentHash: 'd'.repeat(64), outcome: 'SUBMITTED', invoiceId: invoice, draftId: null, submissionId: submission, decisionId: null,
      journalId: '99999999-9999-4999-8999-999999999999', draft: null,
      receipt: { commandId: exactBody.commandId, invoiceId: invoice, submissionId: submission, kind: 'SUBMIT', actorUserId: actor, intentHash: 'd'.repeat(64), outcome: 'SUBMITTED', decisionId: null }, postingReceipt: null });
    expect(c.pendingSubmit()).toBeNull();
    expect(c.error()).toContain('submission was confirmed');
    flushHistoryReads();
  });

  it('acknowledges a lost RETURN decision from its retained review outcome', () => {
    const fixture = open(), c = fixture.componentInstance;
    c.activeReview.set({ invoiceId: invoice, submissionId: submission, journalId: account, journalRevision: '2', digest: 'e'.repeat(64), canPost: true,
      postingBlock: null, duplicateWarning: false, manifest: { supplierInvoiceReference: 'SUP-001', voucherReference: 'PV-001', lateArrival: false, duplicateWarnings: [], lines: [] } });
    c.reviewReason = 'Please correct the expense coding.';
    c.review({ invoiceId: invoice, supplierId: supplier, supplierInvoiceReference: 'SUP-001', voucherReference: 'PV-001', state: 'SUBMITTED', currency: 'QAR', gross: '100.000000', dueDate: '2026-02-10', receiptDate: '2026-01-20', lateArrival: false,
      duplicateWarning: false, submissionId: submission, journalId: account, makerId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', decision: null, decisionReason: null, duplicateResolutionReason: null, openItemCreated: false }, 'RETURN');
    const send = http.expectOne(`${base}/purchase-invoice-reviews`), commandId = send.request.body.commandId;
    send.error(new ProgressEvent('network failure'));
    http.expectOne(`${base}/purchase-invoice-requests/${commandId}?kind=REVIEW`).flush({ commandId, kind: 'REVIEW', actorUserId: actor,
      intentHash: 'f'.repeat(64), outcome: 'RETURNED', invoiceId: invoice, draftId: null, submissionId: submission, decisionId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
      journalId: account, draft: null,
      receipt: { commandId, invoiceId: invoice, submissionId: submission, kind: 'REVIEW', actorUserId: actor, intentHash: 'f'.repeat(64), outcome: 'RETURNED', decisionId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' }, postingReceipt: null });
    expect(c.pendingReview()).toBeNull();
    expect(c.error()).toContain('return decision was acknowledged');
    flushHistoryReads();
  });

  it('selects supplier 26 from the next page and retains it while returning to the previous page', () => {
    const firstPage = Array.from({ length: 25 }, (_, index) => supplierRecord(index === 0 ? supplier : `000000${String(index + 1).padStart(2, '0')}-0000-4000-8000-${String(index + 1).padStart(12, '0')}`));
    const fixture = open(26, firstPage), c = fixture.componentInstance;
    expect(c.supplierPage()).toBe(0);
    expect(c.supplierTotal()).toBe(26);
    c.loadSuppliers(1);
    http.expectOne(`${base}/counterparties?role=SUPPLIER&page=1&pageSize=25`).flush(supplierList(client, 1, 26, [supplierRecord(supplier26)]));
    c.selectSupplier(supplier26);
    c.preview.set({ invoiceId: invoice, revision: '1', currency: 'QAR', net: '10.000000', tax: '0.000000', gross: '10.000000', digest: 'a'.repeat(64), lateArrival: false, duplicateWarnings: [], manifest: { lines: [] } });
    c.submitAssent.set(true);
    c.loadSuppliers(0);
    http.expectOne(`${base}/counterparties?role=SUPPLIER&page=0&pageSize=25`).flush(supplierList(client, 0, 26, firstPage));
    expect(c.supplierId).toBe(supplier26);
    expect(c.supplierOptions().some(item => item.id === supplier26)).toBe(true);
    expect(c.preview()).not.toBeNull();
    expect(c.submitAssent()).toBe(true);
    fixture.destroy();
  });

  it('clears the selected supplier, preview and assent when the client changes', () => {
    const fixture = open(26, [supplierRecord(supplier)]), c = fixture.componentInstance;
    c.selectSupplier(supplier); c.submitAssent.set(true);
    c.preview.set({ invoiceId: invoice, revision: '1', currency: 'QAR', net: '10.000000', tax: '0.000000', gross: '10.000000', digest: 'a'.repeat(64), lateArrival: false, duplicateWarnings: [], manifest: { lines: [] } });
    fixture.componentRef.setInput('clientId', otherClient); fixture.detectChanges();
    expect(c.supplierId).toBe(''); expect(c.selectedSupplier()).toBeNull(); expect(c.preview()).toBeNull(); expect(c.submitAssent()).toBe(false);
    http.expectOne(`/api/ui/accounting/clients/${otherClient}/counterparties?role=SUPPLIER&page=0&pageSize=25`).flush(supplierList(otherClient, 0, 0, []));
    http.expectOne(`/api/ui/accounting/clients/${otherClient}/purchase-invoice-drafts?page=0&pageSize=25`).flush({ clientId: otherClient, page: 0, pageSize: 25, hasMore: false, rows: [] });
    http.expectOne(`/api/ui/accounting/clients/${otherClient}/purchase-invoices`).flush([]);
    expect(c.supplierTotal()).toBe(0); expect(c.supplierPage()).toBe(0);
    fixture.destroy();
  });

  it('ignores an obsolete supplier page response after a newer page request wins', () => {
    const fixture = open(26, [supplierRecord(supplier)]), c = fixture.componentInstance;
    c.loadSuppliers(1);
    const obsolete = http.expectOne(`${base}/counterparties?role=SUPPLIER&page=1&pageSize=25`);
    c.loadSuppliers(0);
    const latest = http.expectOne(`${base}/counterparties?role=SUPPLIER&page=0&pageSize=25`);
    expect(obsolete.cancelled).toBe(true);
    latest.flush(supplierList(client, 0, 1, [supplierRecord(supplier)]));
    expect(c.supplierPage()).toBe(0); expect(c.supplierTotal()).toBe(1); expect(c.suppliers()?.map(item => item.id)).toEqual([supplier]);
    fixture.destroy();
  });
  it('retains the selected supplier after a failed read but blocks preparation until current active authority is verified', () => {
    const fixture = open(), c = fixture.componentInstance;
    c.selectSupplier(supplier); c.draft.set(savedDraft('1', 'Supplier evidence')); c.apRoleId = account;
    c.periodId = period; c.receiptDate = '2026-01-20'; c.documentDate = '2026-01-10'; c.accountingDate = '2026-01-20'; c.supplyDate = '2026-01-10'; c.dueDate = '2026-02-10';
    c.lines = [{ accountCode: '6000', description: 'Supplies', quantity: '1', unitPrice: '100', discount: '0' }];
    c.draftAssent.set(true); expect(c.canUseSupplier()).toBe(true);
    c.loadSuppliers(0);
    http.expectOne(`${base}/counterparties?role=SUPPLIER&page=0&pageSize=25`).error(new ProgressEvent('network failure'));
    expect(c.selectedSupplier()?.id).toBe(supplier); expect(c.canUseSupplier()).toBe(false); expect(c.draftAssent()).toBe(false);
    c.draftAssent.set(true); c.saveDraft(); c.previewDraft();
    http.expectNone(`${base}/purchase-invoice-drafts`); http.expectNone(`${base}/purchase-invoices/${invoice}/preview`);
    c.loadSuppliers(0);
    http.expectOne(`${base}/counterparties?role=SUPPLIER&page=0&pageSize=25`).flush({ ...supplierList(client, 0, 1, [supplierRow]), bookkeepingActive: false });
    expect(c.canUseSupplier()).toBe(false); expect(c.draft()).not.toBeNull(); expect(c.selectedSupplier()?.id).toBe(supplier);
    c.refresh(); http.expectOne(`${base}/purchase-invoices`).flush([]);
    c.loadSuppliers(0);
    http.expectOne(`${base}/counterparties?role=SUPPLIER&page=0&pageSize=25`).flush(supplierList(client, 0, 1, [supplierRow]));
    expect(c.canUseSupplier()).toBe(true); expect(c.draftAssent()).toBe(false);
    fixture.destroy();
  });
});
