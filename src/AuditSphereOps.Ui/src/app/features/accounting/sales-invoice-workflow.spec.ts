import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { SalesInvoiceWorkflow, SalesInvoiceLifecycle, decodeInvoiceLifecycle, decodeInvoiceSubmissionPreview, decodeInvoiceReviewPreview } from './sales-invoice-workflow';
import { SessionService } from '../../core/session';
const client = '11111111-1111-4111-8111-111111111111';
const invoice = '22222222-2222-4222-8222-222222222222';
const maker = '33333333-3333-4333-8333-333333333333';
const reviewer = '44444444-4444-4444-8444-444444444444';
const ar = '55555555-5555-4555-8555-555555555555';
const revenue = '66666666-6666-4666-8666-666666666666';
const draft = '77777777-7777-4777-8777-777777777777';
const submission = '88888888-8888-4888-8888-888888888888';
const journal = '99999999-9999-4999-8999-999999999999';
const base = `/api/ui/accounting/clients/${client}`;
const manifest = { version: 'client-sales-submission-v1', draftId: draft, draftHash: 'a'.repeat(64), profileId: client, profileRevision: '9007199254740993', receivableRoleId: ar, receivableDecisionId: reviewer, receivableAccountId: ar, noTaxReason: '', sourceReceiptId: null, sourceReceiptHash: null, sourceBasis: 'Client authorized preparation facts', mandateId: client, mandateGeneration: '9007199254740994', lines: [
  { lineNumber: 1, accountId: ar, accountCode: '1200', accountName: 'Trade receivables', description: 'Invoice receivable', debit: '250.000000', credit: '0.000000' },
  { lineNumber: 2, accountId: revenue, accountCode: '4000', accountName: 'Revenue', description: 'Invoice revenue', debit: '0.000000', credit: '250.000000' }
] };
const emptyLifecycle: SalesInvoiceLifecycle = { clientId: client, invoiceId: invoice, draftRevision: '1', state: 'DRAFT', bookkeepingActive: true, canRevise: true, canSubmit: true, canReview: false, posted: false, issued: false, deliveryState: 'NOT_REQUESTED', submissionId: null, journalId: null, journalRevision: null, makerId: maker, decision: null, decisionReason: null, manifestHash: null, manifest: null, openAmount: null, dueDate: null, periodId: client, currency: 'QAR', sourceLines: [] };
const submitted: SalesInvoiceLifecycle = { ...emptyLifecycle, state: 'SUBMITTED', canRevise: false, canSubmit: false, canReview: true, submissionId: submission, journalId: journal, journalRevision: '2', manifestHash: 'b'.repeat(64), manifest };
const posted: SalesInvoiceLifecycle = { ...submitted, state: 'POSTED', canReview: false, posted: true, journalRevision: '3', decision: 'APPROVE', decisionReason: 'Independent check', openAmount: '250.00', dueDate: '2026-02-10' };
const preview = { invoiceId: invoice, draftId: draft, draftRevision: '1', submissionId: null, currency: 'QAR', gross: '250.00', digest: 'c'.repeat(64), manifest };
const reviewPreview = { invoiceId: invoice, submissionId: submission, journalId: journal, journalRevision: '2', digest: 'd'.repeat(64), canPost: true, postingBlock: null, reviewContextJson: '{"Version":"client-sales-review-v1"}', manifest };
function receipt(body: Record<string, unknown>, kind: 'SUBMIT' | 'REVIEW', actor = maker) { return { commandId: body['commandId'], invoiceId: invoice, submissionId: submission, kind, actorUserId: actor, intentHash: 'e'.repeat(64), outcome: kind === 'SUBMIT' ? 'SUBMITTED' : body['decision'] === 'APPROVE' ? 'POSTED' : 'RETURNED', decisionId: kind === 'REVIEW' ? reviewer : null }; }

describe('Client sales invoice lifecycle and exact preview contracts', () => {
  it('separates current posting from issue/delivery and preserves large string revisions', () => {
    expect(decodeInvoiceLifecycle(posted, client, invoice).issued).toBe(false);
    expect(decodeInvoiceSubmissionPreview(preview, invoice, '1').manifest.profileRevision).toBe('9007199254740993');
    expect(decodeInvoiceReviewPreview(reviewPreview, submitted).journalRevision).toBe('2');
  });
  it('rejects wrong scopes, numeric revisions, inconsistent lifecycle and invented issue/delivery claims', () => {
    for (const changed of [{ clientId: reviewer }, { invoiceId: reviewer }, { draftRevision: 1 }, { issued: true }, { deliveryState: 'DELIVERED' }, { posted: true }, { canReview: true }, { makerId: null }]) expect(() => decodeInvoiceLifecycle({ ...emptyLifecycle, ...changed }, client, invoice)).toThrow();
    expect(() => decodeInvoiceLifecycle({ ...posted, openAmount: 250 }, client, invoice)).toThrow();
    expect(() => decodeInvoiceLifecycle({ ...submitted, canRevise: true }, client, invoice)).toThrow();
  });
  it('rejects numeric money, unbalanced lines, duplicate accounts, wrong AR direction and source inconsistencies', () => {
    const badManifest = [
      { ...manifest, profileRevision: 1 }, { ...manifest, mandateGeneration: 1 },
      { ...manifest, lines: [{ ...manifest.lines[0], debit: 250 }, manifest.lines[1]] },
      { ...manifest, lines: [manifest.lines[0], { ...manifest.lines[1], credit: '249.999999' }] },
      { ...manifest, lines: [manifest.lines[0], { ...manifest.lines[1], accountId: ar }] },
      { ...manifest, lines: [{ ...manifest.lines[0], debit: '0', credit: '250' }, { ...manifest.lines[1], debit: '250', credit: '0' }] },
      { ...manifest, noTaxReason: 4 }, { ...manifest, sourceReceiptHash: 'a'.repeat(64) }, { ...manifest, sourceBasis: '' },
      { ...manifest, sourceReceiptId: reviewer, sourceReceiptHash: null }
    ];
    for (const m of badManifest) expect(() => decodeInvoiceSubmissionPreview({ ...preview, manifest: m }, invoice, '1')).toThrow();
    expect(() => decodeInvoiceSubmissionPreview({ ...preview, gross: '250.01' }, invoice, '1')).toThrow();
    expect(() => decodeInvoiceSubmissionPreview(preview, invoice, '2')).toThrow();
  });
  it('binds review to the exact journal revision and frozen manifest', () => {
    expect(() => decodeInvoiceReviewPreview({ ...reviewPreview, journalRevision: '3' }, submitted)).toThrow();
    expect(() => decodeInvoiceReviewPreview({ ...reviewPreview, submissionId: reviewer }, submitted)).toThrow();
    expect(() => decodeInvoiceReviewPreview({ ...reviewPreview, manifest: { ...manifest, noTaxReason: 'Changed reason' } }, submitted)).toThrow();
  });
});

describe('Client invoice guarded workflow and command recovery', () => {
  let http: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({ imports: [SalesInvoiceWorkflow], providers: [provideHttpClient(), provideHttpClientTesting()] }); http = TestBed.inject(HttpTestingController); });
  afterEach(() => { http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); });
  function setup(w = emptyLifecycle, actor = maker) {
    TestBed.inject(SessionService).current.set({ userId: actor, firmId: client, generation: '1', staff: true });
    const fixture = TestBed.createComponent(SalesInvoiceWorkflow); fixture.componentRef.setInput('clientId', client); fixture.componentRef.setInput('invoiceId', invoice); fixture.componentRef.setInput('preparationRevision', '1'); fixture.detectChanges();
    http.expectOne(`${base}/sales-invoices/${invoice}/lifecycle`).flush(w);
    return { fixture, c: fixture.componentInstance };
  }
  function prepare(c: SalesInvoiceWorkflow) {
    c.receivableRoleId = ar; c.noTaxReason = ''; c.sourceBasis = manifest.sourceBasis; c.previewSubmission();
    const request = http.expectOne(`${base}/sales-invoices/${invoice}/preview`);
    expect(request.request.body.expectedDraftRevision).toBe('1'); expect(request.request.body.sourceReceiptId).toBeNull();
    request.flush(preview); c.submitAssent.set(true); c.submit();
    return http.expectOne(`${base}/sales-invoices/${invoice}/submit`);
  }
  it('recovers an accepted lost submission without another POST and refreshes separate lifecycle truth', () => {
    const { c, fixture } = setup(); const first = prepare(c), body = first.request.body;
    first.error(new ProgressEvent('lost')); fixture.detectChanges(); expect(c.pending()).not.toBeNull();
    expect(fixture.nativeElement.querySelector('input[name="arRole"]')?.matches(':disabled')).toBe(true);
    c.submit(); http.expectNone(`${base}/sales-invoices/${invoice}/submit`);
    c.recover(); const recovered = http.expectOne(`${base}/sales-invoice-requests/${body.commandId}?kind=SUBMIT`); recovered.flush(receipt(body, 'SUBMIT'));
    http.expectOne(`${base}/sales-invoices/${invoice}/lifecycle`).flush({ ...submitted, canReview: false });
    expect(c.pending()).toBeNull(); expect(c.lifecycle()?.state).toBe('SUBMITTED'); expect(c.lifecycle()?.issued).toBe(false); expect(c.receipt()?.outcome).toBe('SUBMITTED');
  });
  it('requires a 404 and explicit retry assent and retains the original immutable command body', () => {
    const { c } = setup(); const first = prepare(c), body = structuredClone(first.request.body); first.error(new ProgressEvent('lost'));
    c.sendPending(); http.expectNone(`${base}/sales-invoices/${invoice}/submit`);
    c.recover(); http.expectOne(`${base}/sales-invoice-requests/${body.commandId}?kind=SUBMIT`).flush({}, { status: 404, statusText: 'Not found' });
    c.noTaxReason = 'Changed local field'; c.sourceBasis = 'Changed source'; c.changed(); c.sendPending(); http.expectNone(`${base}/sales-invoices/${invoice}/submit`);
    c.retryAssent.set(true); c.sendPending(); const repeated = http.expectOne(`${base}/sales-invoices/${invoice}/submit`); expect(repeated.request.body).toEqual(body);
    repeated.flush(receipt(body, 'SUBMIT')); http.expectOne(`${base}/sales-invoices/${invoice}/lifecycle`).flush({ ...submitted, canReview: false }); expect(c.pending()).toBeNull();
  });
  it('keeps mismatched command receipts locked and never invents a successful posting', () => {
    const { c } = setup(); const first = prepare(c), body = first.request.body;
    first.flush({ ...receipt(body, 'SUBMIT'), commandId: reviewer }); expect(c.pending()).not.toBeNull(); expect(c.receipt()).toBeNull(); expect(c.lifecycle()?.posted).toBe(false);
    http.expectNone(`${base}/sales-invoices/${invoice}/lifecycle`);
  });
  it('requires another reviewer and preserves review command identity after a lost response', () => {
    const { c } = setup(submitted, reviewer); c.previewReview(); http.expectOne(`${base}/sales-invoice-submissions/${submission}/preview`).flush(reviewPreview);
    c.decision = 'APPROVE'; c.reviewReason = 'Independently checked the exact source and posting'; c.reviewAssent.set(true); c.review();
    const first = http.expectOne(`${base}/sales-invoice-reviews`), body = first.request.body;
    expect(body.submissionId).toBe(submission); expect(body.previewDigest).toBe(reviewPreview.digest); first.error(new ProgressEvent('lost'));
    c.recover(); http.expectOne(`${base}/sales-invoice-requests/${body.commandId}?kind=REVIEW`).flush(receipt(body, 'REVIEW', reviewer));
    http.expectOne(`${base}/sales-invoices/${invoice}/lifecycle`).flush(posted);
    expect(c.lifecycle()?.posted).toBe(true); expect(c.lifecycle()?.issued).toBe(false); expect(c.lifecycle()?.deliveryState).toBe('NOT_REQUESTED');
  });
  it('prevents self-review and permits only RETURN when the fresh context blocks posting', () => {
    const { c } = setup(submitted, maker); c.previewReview(); http.expectNone(`${base}/sales-invoice-submissions/${submission}/preview`);
    TestBed.inject(SessionService).current.set({ userId: reviewer, firmId: client, generation: '2', staff: true }); TestBed.tick();
    http.expectOne(`${base}/sales-invoices/${invoice}/lifecycle`).flush(submitted);
    c.previewReview(); http.expectOne(`${base}/sales-invoice-submissions/${submission}/preview`).flush({ ...reviewPreview, canPost: false, postingBlock: 'period.closed' });
    c.decision = 'APPROVE'; c.reviewReason = 'Stale context'; c.reviewAssent.set(true); c.review(); http.expectNone(`${base}/sales-invoice-reviews`);
    c.decision = 'RETURN'; c.reviewAssent.set(true); c.review(); const returned = http.expectOne(`${base}/sales-invoice-reviews`); returned.flush(receipt(returned.request.body, 'REVIEW', reviewer));
    http.expectOne(`${base}/sales-invoices/${invoice}/lifecycle`).flush({ ...submitted, state: 'RETURNED', canReview: false, decision: 'RETURN', decisionReason: 'Stale context' });
    expect(c.lifecycle()?.state).toBe('RETURNED');
  });
  it('cancels and clears pending data on client change or session invalidation', () => {
    const { c, fixture } = setup(); const old = prepare(c); fixture.componentRef.setInput('clientId', reviewer); fixture.detectChanges();
    expect(old.cancelled).toBe(true); expect(c.pending()).toBeNull(); expect(c.lifecycle()).toBeNull(); expect(c.submissionPreview()).toBeNull();
    http.expectOne(`/api/ui/accounting/clients/${reviewer}/sales-invoices/${invoice}/lifecycle`).flush({ ...emptyLifecycle, clientId: reviewer });
    TestBed.inject(SessionService).clear(); fixture.detectChanges(); expect(c.lifecycle()).toBeNull(); expect(c.receipt()).toBeNull(); expect(c.submitAssent()).toBe(false);
  });
  it('clears previous preview and edit eligibility when preparation revision changes', () => {
    const { c, fixture } = setup(); c.receivableRoleId = ar; c.noTaxReason = ''; c.sourceBasis = manifest.sourceBasis; c.previewSubmission(); http.expectOne(`${base}/sales-invoices/${invoice}/preview`).flush(preview);
    fixture.componentRef.setInput('preparationRevision', '2'); fixture.detectChanges(); expect(c.submissionPreview()).toBeNull(); expect(c.lifecycle()).toBeNull();
    http.expectOne(`${base}/sales-invoices/${invoice}/lifecycle`).flush({ ...emptyLifecycle, draftRevision: '2' }); expect(c.lifecycle()?.draftRevision).toBe('2');
  });
});
