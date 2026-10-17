import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OpenItemAllocations } from './open-item-allocations';
import { SessionService } from '../../core/session';

const client = '11111111-1111-4111-8111-111111111111';
const otherClient = '99999999-9999-4999-8999-999999999999';
const period = '22222222-2222-4222-8222-222222222222';
const sourceId = '33333333-3333-4333-8333-333333333333';
const sourceDocumentId = '44444444-4444-4444-8444-444444444444';
const partyId = '55555555-5555-4555-8555-555555555555';
const targetId = '66666666-6666-4666-8666-666666666666';
const secondTargetId = '77777777-7777-4777-8777-777777777777';
const submissionId = '88888888-8888-4888-8888-888888888888';
const reviewerId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const nextReviewerId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const originalAllocationLineId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';

function openItem(kind: string, id: string, documentId: string, original: string, open: string) {
  return { kind, openItemId: id, sourceDocumentId: documentId, counterpartyId: partyId, counterpartyName: 'Synthetic customer',
    currency: 'QAR', originalAmount: original, appliedAmount: '0.000000', openAmount: open, dueDate: null, asOfDate: '2026-01-01', status: 'OPEN' };
}
function retainedPreview() {
  return { submissionId, digest: 'b'.repeat(64), disposition: 'ALLOCATE', sourceKind: 'SALES_RECEIPT', sourceItemId: sourceId,
    counterpartyId: partyId, currency: 'QAR', sourceOriginalAmount: '150.000000', sourceAvailableAmount: '100.000000',
    lines: [
      { lineNumber: 1, targetKind: 'SALES_INVOICE', targetOpenItemId: targetId, amount: '25.000000', reversesAllocationLineId: null },
      { lineNumber: 2, targetKind: 'SALES_INVOICE', targetOpenItemId: secondTargetId, amount: '35.000000', reversesAllocationLineId: null }
    ], targetAvailableAmounts: ['80.000000', '90.000000'] };
}

describe('approved allocation unallocation', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [OpenItemAllocations], providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: reviewerId, firmId: client, generation: '1', staff: true });
  });
  afterEach(() => { try { http.verify({ ignoreCancelled: true }); } finally { TestBed.resetTestingModule(); sessionStorage.clear(); } });
  async function openHistory() {
    const fixture = TestBed.createComponent(OpenItemAllocations);
    fixture.componentRef.setInput('clientId', client); fixture.detectChanges();
    http.expectOne(request => request.url.endsWith('/open-item-balances')).flush([]);
    http.expectOne(request => request.url.endsWith('/manual-settlement-options')).flush({ periods: [], counterparties: [], cashAccounts: [] });
    http.expectOne(request => request.url.endsWith('/open-item-allocation-submissions')).flush([]);
    await fixture.whenStable();
    const c = fixture.componentInstance; c.loadAllocationHistory();
    http.expectOne(`/api/ui/accounting/clients/${client}/open-item-allocations/history?page=0&pageSize=25`).flush({
      clientId: client, page: 0, pageSize: 25, total: 1, bookkeepingActive: true, lines: [{
        allocationLineId: originalAllocationLineId, submissionId, sourceKind: 'SALES_RECEIPT', sourceItemId: sourceId,
        targetKind: 'SALES_INVOICE', targetOpenItemId: targetId, counterpartyId: partyId, counterpartyName: 'Synthetic customer', currency: 'QAR',
        originalAmount: '20.000000', reversedAmount: '7.000000', reversibleAmount: '13.000000', reference: 'ORIGINAL-ALLOC', reason: 'Original reviewed allocation',
        manifestHash: 'a'.repeat(64), createdByUserId: reviewerId, approvedByUserId: nextReviewerId, approvedAt: '2026-01-10T12:00:00Z'
      }] });
    c.selectUnallocation(c.allocationHistory()!.lines[0]);
    return { fixture, c };
  }
  it.each(['5.000000', '13.000000'])('pins the exact original line for a %s partial or full remaining reversal and reuses recovery', async amount => {
    const { fixture, c } = await openHistory();
    expect(c.amount).toBe('13.000000');
    c.amount = amount; c.reference = 'UNALLOC-REVIEW'; c.reason = 'Correct the approved allocation'; c.allocationChanged(); c.previewAllocation();
    const preview = http.expectOne(`/api/ui/accounting/clients/${client}/open-item-allocations/preview`);
    expect(preview.request.body).toMatchObject({ disposition: 'UNALLOCATE', sourceKind: 'SALES_RECEIPT', sourceItemId: sourceId,
      lines: [{ lineNumber: 1, targetKind: 'SALES_INVOICE', targetOpenItemId: targetId, amount, reversesAllocationLineId: originalAllocationLineId }] });
    preview.flush({ ...retainedPreview(), submissionId: '00000000-0000-0000-0000-000000000000', disposition: 'UNALLOCATE',
      lines: [{ lineNumber: 1, targetKind: 'SALES_INVOICE', targetOpenItemId: targetId, amount, reversesAllocationLineId: originalAllocationLineId }],
      targetAvailableAmounts: ['80.000000'] });
    c.setMakerAssent(true); expect(c.canSubmitAllocation()).toBe(true);
    await c.submitAllocation();
    const submission = http.expectOne(`/api/ui/accounting/clients/${client}/open-item-allocations/submit`);
    expect(submission.request.body.disposition).toBe('UNALLOCATE');
    expect(submission.request.body.lines[0].reversesAllocationLineId).toBe(originalAllocationLineId);
    submission.error(new ProgressEvent('lost response'));
    expect(c.hasUnresolvedSubmission()).toBe(true);
    const reference = JSON.parse(sessionStorage.getItem('auditsphere:open-item-allocation-recovery:v1')!)[0];
    expect(reference.commandId).toBe(submission.request.body.commandId); expect(reference.fingerprint).toMatch(/^[a-f0-9]{64}$/);
    expect(reference.body).toBeUndefined(); expect(reference.lines).toBeUndefined(); expect(reference.reason).toBeUndefined();
    fixture.destroy();
  });
  it('rejects over-reversal and a mismatched original line, and discards stale preview assent', async () => {
    const { fixture, c } = await openHistory();
    c.amount = '14'; c.reference = 'UNALLOC-STALE'; c.reason = 'Correct the approved allocation'; c.previewAllocation();
    http.expectNone(`/api/ui/accounting/clients/${client}/open-item-allocations/preview`);
    c.amount = '5'; c.previewAllocation();
    const mismatched = http.expectOne(`/api/ui/accounting/clients/${client}/open-item-allocations/preview`);
    mismatched.flush({ ...retainedPreview(), submissionId: '00000000-0000-0000-0000-000000000000', disposition: 'UNALLOCATE',
      lines: [{ lineNumber: 1, targetKind: 'SALES_INVOICE', targetOpenItemId: targetId, amount: '5.000000', reversesAllocationLineId: nextReviewerId }], targetAvailableAmounts: ['80.000000'] });
    expect(c.preview()).toBeNull(); expect(c.canSubmitAllocation()).toBe(false);
    c.previewAllocation();
    http.expectOne(`/api/ui/accounting/clients/${client}/open-item-allocations/preview`).flush({ code: 'stale' }, { status: 400, statusText: 'Bad Request' });
    c.setMakerAssent(true); expect(c.preview()).toBeNull(); expect(c.canSubmitAllocation()).toBe(false);
    fixture.destroy();
  });
});

describe('client open-item control reconciliation', () => {
  it('offers same-party, same-currency opening invoices as reviewed allocation targets', () => {
    const source = { kind: 'SALES_RECEIPT', openItemId: 'receipt', counterpartyId: 'party-a', currency: 'QAR', openAmount: '20.000000' };
    const rows = [
      { kind: 'SALES_INVOICE', openItemId: 'sales', counterpartyId: 'party-a', currency: 'QAR', openAmount: '30.000000' },
      { kind: 'OPENING_AR_INVOICE', openItemId: 'opening-ar', counterpartyId: 'party-a', currency: 'QAR', openAmount: '40.000000' },
      { kind: 'OPENING_AP_INVOICE', openItemId: 'opening-ap', counterpartyId: 'party-a', currency: 'QAR', openAmount: '50.000000' },
      { kind: 'OPENING_AR_INVOICE', openItemId: 'other-party', counterpartyId: 'party-b', currency: 'QAR', openAmount: '60.000000' },
      { kind: 'OPENING_AR_INVOICE', openItemId: 'other-currency', counterpartyId: 'party-a', currency: 'USD', openAmount: '70.000000' }
    ];
    const component = Object.assign(Object.create(OpenItemAllocations.prototype), {
      sourceId: 'receipt', balances: () => rows, credits: () => [source]
    }) as OpenItemAllocations;

    expect(component.targets().map(x => x.openItemId)).toEqual(['sales', 'opening-ar']);
  });

  it('loads the scoped as-of control report and distinguishes when no approved opening applies', () => {
    TestBed.configureTestingModule({ imports: [OpenItemAllocations], providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(OpenItemAllocations);
    const component = fixture.componentInstance;
    component.asOfDate = '2026-02-01';
    fixture.componentRef.setInput('clientId', client);
    fixture.componentRef.setInput('periods', [{ id: period, code: 'FY26', startDate: '2026-01-01', endDate: '2026-12-31', currency: 'QAR' }]);
    fixture.detectChanges();

    http.expectOne(request => request.url.endsWith('/open-item-balances') && request.params.get('asOf') === '2026-02-01').flush([]);
    http.expectOne(request => request.url.endsWith('/manual-settlement-options')).flush({ periods: [], counterparties: [], cashAccounts: [] });
    http.expectOne(request => request.url.endsWith('/open-item-control-reconciliation') &&
      request.params.get('periodId') === period && request.params.get('asOf') === '2026-02-01').flush({
      clientId: client, periodId: period, periodCode: 'FY26', currency: 'QAR', asOfDate: '2026-02-01',
      ledgerBasis: 'POSTED_NATIVE_CLIENT_JOURNALS_TO_AS_OF_DATE',
      allocationBasis: 'CURRENT_APPROVED_ALLOCATIONS_NO_SEPARATE_EFFECTIVE_DATE',
      openingDetailStatus: 'NO_APPROVED_OPENING', unlinkedOpenItemCount: 0, accounts: [], reconciled: true
    });
    http.expectOne(request => request.url.endsWith('/open-item-allocation-submissions')).flush([]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('AR/AP balances reconcile.');
    expect(fixture.nativeElement.textContent).toContain('No approved cutover opening applies to this period.');
    http.verify();
    fixture.destroy();
  });
});

describe('client open-item allocation manifest assent', () => {
  it('clears maker assent after an edit and requires an independent reviewer to assent to the exact client-scoped retained lines', async () => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [OpenItemAllocations], providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    const session = TestBed.inject(SessionService);
    session.current.set({ userId: reviewerId, firmId: client, generation: '4', staff: true });
    const fixture = TestBed.createComponent(OpenItemAllocations);
    fixture.componentRef.setInput('clientId', client);
    await fixture.whenStable();
    http.expectOne(request => request.url.endsWith('/open-item-balances')).flush([
      openItem('SALES_RECEIPT', sourceId, sourceDocumentId, '150.000000', '150.000000'),
      openItem('SALES_INVOICE', targetId, targetId, '200.000000', '200.000000'),
      openItem('SALES_INVOICE', secondTargetId, secondTargetId, '300.000000', '300.000000')
    ]);
    http.expectOne(request => request.url.endsWith('/manual-settlement-options')).flush({ periods: [], counterparties: [], cashAccounts: [] });
    http.expectOne('/api/ui/accounting/clients/' + client + '/open-item-allocation-submissions').flush([retainedPreview()]);
    await fixture.whenStable();

    const component = fixture.componentInstance;
    component.sourceId = sourceId; component.targetId = targetId; component.amount = '100';
    component.reference = 'RCPT-2026-01'; component.reason = 'Apply received customer funds';
    component.previewAllocation();
    const previewRequest = http.expectOne({ method: 'POST', url: '/api/ui/accounting/clients/' + client + '/open-item-allocations/preview' });
    expect(previewRequest.request.body).toMatchObject({ commandId: expect.any(String), sourceItemId: sourceId, reference: 'RCPT-2026-01',
      reason: 'Apply received customer funds', lines: [{ targetOpenItemId: targetId, amount: '100' }] });
    const previewCommandId = previewRequest.request.body.commandId;
    previewRequest.flush({ ...retainedPreview(), submissionId: '00000000-0000-0000-0000-000000000000', digest: 'a'.repeat(64),
      sourceAvailableAmount: '150.000000', lines: [{ lineNumber: 1, targetKind: 'SALES_INVOICE', targetOpenItemId: targetId,
        amount: '100.000000', reversesAllocationLineId: null }], targetAvailableAmounts: ['200.000000'] });
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('100.000000 QAR');
    expect(component.canSubmitAllocation()).toBe(false);
    component.setMakerAssent(true);
    expect(component.canSubmitAllocation()).toBe(true);
    await component.submitAllocation();
    const submitRequest = http.expectOne({ method: 'POST', url: '/api/ui/accounting/clients/' + client + '/open-item-allocations/submit' });
    expect(submitRequest.request.body).toEqual({ commandId: previewCommandId, sourceKind: 'SALES_RECEIPT', sourceItemId: sourceId,
      disposition: 'ALLOCATE', reference: 'RCPT-2026-01', reason: 'Apply received customer funds',
      lines: [{ lineNumber: 1, targetKind: 'SALES_INVOICE', targetOpenItemId: targetId, amount: '100.000000', reversesAllocationLineId: null }],
      previewDigest: 'a'.repeat(64), reviewed: true });
    submitRequest.flush({ code: 'gateway-timeout' }, { status: 504, statusText: 'Gateway Timeout' });
    await fixture.whenStable();
    expect(component.hasUnresolvedSubmission()).toBe(true);
    expect(component.canSubmitAllocation()).toBe(false);
    const submitRecovery = component.visibleRecoveries().find(ref => ref.kind === 'SUBMIT')!;
    component.recoverCommand(submitRecovery);
    const wrongSubmitLookup = http.expectOne(request => request.url.includes('/open-item-allocation-requests/') && request.params.get('kind') === 'SUBMIT');
    wrongSubmitLookup.flush({ found: true, receipt: { kind: 'SUBMIT', clientId: otherClient, actorUserId: nextReviewerId, commandId: previewCommandId,
      submissionId, decisionId: null, outcome: 'SUBMITTED', previewDigest: 'a'.repeat(64), intentHash: 'd'.repeat(64), sourceKind: 'SALES_RECEIPT',
      sourceItemId: sourceId, disposition: 'ALLOCATE', reference: 'RCPT-2026-01', reason: 'Apply received customer funds',
      lines: [{ lineNumber: 1, targetKind: 'SALES_INVOICE', targetOpenItemId: targetId, amount: '100.000000', reversesAllocationLineId: null }], decision: null, decisionReason: null } });
    await fixture.whenStable();
    expect(component.recoveryMatched()['SUBMIT:' + previewCommandId]).toBe(false);
    expect(component.hasUnresolvedSubmission()).toBe(true);
    component.recoverCommand(submitRecovery);
    const matchingSubmitLookup = http.expectOne(request => request.url.includes('/open-item-allocation-requests/') && request.params.get('kind') === 'SUBMIT');
    matchingSubmitLookup.flush({ found: true, receipt: { kind: 'SUBMIT', clientId: client, actorUserId: reviewerId, commandId: previewCommandId,
      submissionId, decisionId: null, outcome: 'SUBMITTED', previewDigest: 'a'.repeat(64), intentHash: 'd'.repeat(64), sourceKind: 'SALES_RECEIPT',
      sourceItemId: sourceId, disposition: 'ALLOCATE', reference: 'RCPT-2026-01', reason: 'Apply received customer funds',
      lines: [{ lineNumber: 1, targetKind: 'SALES_INVOICE', targetOpenItemId: targetId, amount: '100.000000', reversesAllocationLineId: null }], decision: null, decisionReason: null } });
    await fixture.whenStable();
    expect(component.recoveryMatched()['SUBMIT:' + previewCommandId]).toBe(true);
    component.acknowledgeRecovery(submitRecovery);
    expect(component.hasUnresolvedSubmission()).toBe(false);
    component.setMakerAssent(true);
    expect(component.canSubmitAllocation()).toBe(false);
    component.amount = '200';
    component.allocationChanged();
    expect(component.preview()).toBeNull();
    expect(component.reviewed).toBe(false);
    expect(component.canSubmitAllocation()).toBe(false);
    component.submitAllocation();
    http.expectNone(request => request.method === 'POST' && request.url.endsWith('/open-item-allocations/submit'));

    const retained = component.pending()[0];
    expect(retained.submissionId).toBe(submissionId);
    const rendered = fixture.nativeElement.textContent as string;
    expect(rendered).toContain(targetId);
    expect(rendered).toContain(secondTargetId);
    expect(rendered).toContain('25.000000 QAR');
    expect(rendered).toContain('35.000000 QAR');
    expect(rendered).toContain('60.000000 QAR');
    expect(rendered).toContain(retained.digest);
    component.reviewReason[submissionId] = 'Reviewed both retained targets';

    const stale = { ...retained, digest: 'c'.repeat(64) };
    component.setReviewAssent(stale, true);
    component.review(stale, 'APPROVE');
    http.expectNone(request => request.method === 'POST' && request.url.endsWith('/open-item-allocation-reviews'));

    component.setReviewAssent(retained, true);
    expect(component.reviewAssented(retained)).toBe(true);
    await component.review(retained, 'APPROVE');
    const reviewRequest = http.expectOne({ method: 'POST', url: '/api/ui/accounting/clients/' + client + '/open-item-allocation-reviews' });
    expect(reviewRequest.request.body).toMatchObject({ submissionId, previewDigest: retained.digest, reviewed: true, decision: 'APPROVE' });
    reviewRequest.flush({ code: 'gateway-timeout' }, { status: 504, statusText: 'Gateway Timeout' });
    await fixture.whenStable();
    const reviewRecovery = component.visibleRecoveries().find(ref => ref.kind === 'REVIEW')!;
    expect(component.hasUnresolvedReview(submissionId)).toBe(true);
    component.review(retained, 'RETURN');
    http.expectNone(request => request.method === 'POST' && request.url.endsWith('/open-item-allocation-reviews'));
    component.recoverCommand(reviewRecovery);
    const reviewLookup = http.expectOne(request => request.url.includes('/open-item-allocation-requests/') && request.params.get('kind') === 'REVIEW');
    reviewLookup.flush({ found: true, receipt: { kind: 'REVIEW', clientId: client, actorUserId: reviewerId, commandId: reviewRecovery.commandId,
      submissionId, decisionId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', outcome: 'APPROVED', previewDigest: retained.digest, intentHash: 'e'.repeat(64),
      sourceKind: retained.sourceKind, sourceItemId: retained.sourceItemId, disposition: retained.disposition, reference: 'RCPT-2026-01',
      reason: 'Apply received customer funds', lines: retained.lines, decision: 'APPROVE', decisionReason: 'Reviewed both retained targets' } });
    await fixture.whenStable();
    expect(component.recoveryMatched()['REVIEW:' + reviewRecovery.commandId]).toBe(true);
    component.acknowledgeRecovery(reviewRecovery);
    expect(component.hasUnresolvedReview(submissionId)).toBe(false);
    http.expectOne(request => request.url.endsWith('/open-item-balances')).flush([]);
    http.expectOne(request => request.url.endsWith('/open-item-allocation-submissions')).flush([]);

    session.current.set({ userId: nextReviewerId, firmId: client, generation: '4', staff: true });
    await fixture.whenStable();
    http.expectOne(request => request.url.endsWith('/open-item-balances')).flush([]);
    http.expectOne(request => request.url.endsWith('/manual-settlement-options')).flush({ periods: [], counterparties: [], cashAccounts: [] });
    http.expectOne(request => request.url.endsWith('/open-item-allocation-submissions')).flush([retainedPreview()]);
    await fixture.whenStable();
    component.reviewReason[submissionId] = 'Reviewed current retained manifest';
    expect(component.reviewAssented(retained)).toBe(false);
    component.review(retained, 'APPROVE');
    http.expectNone(request => request.method === 'POST' && request.url.endsWith('/open-item-allocation-reviews'));

    fixture.componentRef.setInput('clientId', otherClient);
    await fixture.whenStable();
    http.expectOne(request => request.url.endsWith('/open-item-balances')).flush([]);
    http.expectOne(request => request.url.endsWith('/manual-settlement-options')).flush({ periods: [], counterparties: [], cashAccounts: [] });
    http.expectOne('/api/ui/accounting/clients/' + otherClient + '/open-item-allocation-submissions').flush([]);
    await fixture.whenStable();
    component.review(retained, 'APPROVE');
    http.expectNone(request => request.method === 'POST' && request.url.endsWith('/open-item-allocation-reviews'));
    expect(component.pending()).toEqual([]);
    fixture.destroy();
    sessionStorage.clear();
    http.verify();
  });
});

describe('manual settlement draft recovery', () => {
  it('recovers an unknown creation after reload, fences edits, and acknowledges only the exact actor/client/hash draft', async () => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ imports: [OpenItemAllocations], providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    const session = TestBed.inject(SessionService);
    session.current.set({ userId: reviewerId, firmId: client, generation: '9', staff: true });

    let fixture = TestBed.createComponent(OpenItemAllocations);
    fixture.componentRef.setInput('clientId', client);
    await fixture.whenStable();
    http.expectOne(request => request.url.endsWith('/open-item-balances')).flush([]);
    http.expectOne(request => request.url.endsWith('/manual-settlement-options')).flush({ periods: [], counterparties: [], cashAccounts: [] });
    http.expectOne('/api/ui/accounting/clients/' + client + '/open-item-allocation-submissions').flush([]);
    await fixture.whenStable();

    const first = fixture.componentInstance;
    first.settlementPeriodId = period; first.settlementPartyId = partyId; first.settlementCashCode = '1000';
    first.settlementAmount = '42.125'; first.settlementReference = 'EXT-RECEIPT-42'; first.settlementEvidence = 'private evidence reference';
    first.settlementNumber = 'RCT-42'; first.settlementDescription = 'Customer funds already received'; first.settlementReviewed = true;
    first.previewSettlement();
    const previewRequest = http.expectOne({ method: 'POST', url: '/api/ui/accounting/clients/' + client + '/manual-settlements/preview' });
    const commandId = previewRequest.request.body.commandId as string;
    const digest = 'a'.repeat(64), intentHash = 'c'.repeat(64), journalId = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
    previewRequest.flush({ digest, sourceKind: 'SALES_RECEIPT', amount: '42.125000', currency: 'QAR', controlAccountCode: '1100',
      cashAccountCode: '1000', reference: 'EXT-RECEIPT-42', evidenceReference: 'private evidence reference' },
      { headers: { 'X-AuditSphere-Intent-Hash': intentHash } });
    await fixture.whenStable();
    first.createSettlement();
    http.expectOne({ method: 'POST', url: '/api/ui/accounting/clients/' + client + '/manual-settlements' })
      .flush({ code: 'gateway-timeout' }, { status: 504, statusText: 'Gateway Timeout' });
    await fixture.whenStable();
    expect(first.hasUnresolvedSettlement()).toBe(true);
    const stored = sessionStorage.getItem('auditsphere:manual-settlement-recovery:v1')!;
    expect(JSON.parse(stored)).toEqual([{ commandId, clientId: client, actorUserId: reviewerId, previewDigest: digest, intentHash }]);
    expect(stored).not.toContain('42.125');
    expect(stored).not.toContain('private evidence reference');
    fixture.destroy();

    fixture = TestBed.createComponent(OpenItemAllocations);
    fixture.componentRef.setInput('clientId', client);
    await fixture.whenStable();
    expect(fixture.componentInstance.hasUnresolvedSettlement()).toBe(true);
    expect((fixture.componentInstance as unknown as { settlementCommandId: string }).settlementCommandId).not.toBe(commandId);
    fixture.componentInstance.previewSettlement();
    http.expectNone(request => request.method === 'POST' && request.url.endsWith('/manual-settlements/preview'));
    fixture.componentInstance.settlementChanged();
    expect(fixture.componentInstance.settlementRecoveries()).toHaveLength(1);

    const lookup = http.expectOne('/api/ui/accounting/clients/' + client + '/manual-settlement-requests/' + commandId);
    lookup.flush({ found: true, clientId: client, actorUserId: reviewerId, commandId, intentHash,
      receipt: { commandId, journalId, outcome: 'DRAFT' } });
    http.expectOne(request => request.url.endsWith('/open-item-balances')).flush([]);
    http.expectOne(request => request.url.endsWith('/manual-settlement-options')).flush({ periods: [], counterparties: [], cashAccounts: [] });
    http.expectOne('/api/ui/accounting/clients/' + client + '/open-item-allocation-submissions').flush([]);
    await fixture.whenStable();
    expect(fixture.componentInstance.settlementRecoveryMatched()[commandId]).toBe(true);
    fixture.componentInstance.acknowledgeSettlement(fixture.componentInstance.visibleSettlementRecoveries()[0]);
    expect(fixture.componentInstance.hasUnresolvedSettlement()).toBe(false);
    expect(fixture.componentInstance.settlementReceipt()).toBe(journalId);
    expect(sessionStorage.getItem('auditsphere:manual-settlement-recovery:v1')).toBe('[]');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('It is not posted and no bank payment was initiated.');
    fixture.destroy();
    sessionStorage.clear();
    http.verify();
  });
});
