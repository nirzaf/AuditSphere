import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { SalesInvoiceDrafts, decodeSalesDraft } from './sales-invoice-drafts';
import { SessionService } from '../../core/session';
const client = '11111111-1111-4111-8111-111111111111';
const id = '22222222-2222-4222-8222-222222222222';
const actor = '33333333-3333-4333-8333-333333333333';
const customer = { id, clientId: client, legalName: 'Customer', displayName: 'Customer', role: 'CUSTOMER', address: 'Original', country: 'QA', taxIdentifier: '', contactDetails: '', paymentTerms: '', defaultCurrency: '', externalSystem: '', externalReference: '', createdByUserId: actor, createdAt: '2026-01-01T00:00:00Z', revision: '1', effectiveAmendmentId: null };
const snapshot = { version: 'client-sales-draft-v1', engineVersion: 'native-invoice-line-net-v1', profileId: id, profileRevision: '1', chartVersionId: id, draftReference: 'DRAFT-1', sourceReference: '', currency: 'QAR', documentDate: '2026-01-10', accountingDate: '2026-01-10', supplyDate: '2026-01-10', dueDate: '2026-02-10', evidenceReference: '', possibleDuplicateSourceReference: false, seller: { clientId: client, legalName: 'Client' }, customer, policy: { currency: 'QAR', decimalPlaces: 2, midpointRule: 'AWAY_FROM_ZERO', residualTreatment: 'REJECT', maximumResidualMinorUnits: 0, roundingAccountCode: '' }, lines: [{ lineNumber: 1, accountId: id, accountCode: '4000', accountName: 'Revenue', description: 'Consulting', quantity: '2.000000', unitPrice: '125.125000', discount: '0.250000', net: '250.00', gross: '250.00' }], net: '250.00', gross: '250.00', taxTreatment: 'NONE' };
const draft = { id, invoiceId: id, clientId: client, periodId: id, revision: '1', previousRevisionId: null, commandId: id, createdByUserId: actor, snapshotHash: 'f'.repeat(64), status: 'DRAFT', posted: false, issued: false, snapshot };
describe('Client sales draft contract', () => {
  it('retains exact money and client seller/customer scope without claiming posting or issue', () => { expect(decodeSalesDraft(draft, client).snapshot.gross).toBe('250.00'); });
  it('rejects owner mismatch, numeric amounts, unsupported policy, changed control totals and posted claims', () => {
    for (const change of [{ clientId: id }, { posted: true }, { issued: true }, { snapshot: { ...snapshot, seller: { ...snapshot.seller, clientId: id } } }, { snapshot: { ...snapshot, customer: { ...customer, clientId: id } } }, { snapshot: { ...snapshot, gross: 250 } }, { snapshot: { ...snapshot, policy: { ...snapshot.policy, decimalPlaces: 7 } } }, { snapshot: { ...snapshot, net: '200.00', gross: '200.00' } }, { snapshot: { ...snapshot, lines: [{ ...snapshot.lines[0], unitPrice: 125.125 }] } }]) expect(() => decodeSalesDraft({ ...draft, ...change }, client)).toThrow();
  });
});
describe('Exact invoice request recovery', () => {
  let http: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({ imports: [SalesInvoiceDrafts], providers: [provideHttpClient(), provideHttpClientTesting()] }); http = TestBed.inject(HttpTestingController); TestBed.inject(SessionService).current.set({ userId: actor, firmId: id, generation: '1', staff: true }); });
  afterEach(() => { http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); });
  function setup() { const fixture = TestBed.createComponent(SalesInvoiceDrafts); fixture.componentRef.setInput('clientId', client); fixture.componentRef.setInput('currency', 'QAR'); fixture.componentRef.setInput('periods', [{ id, code: '2026', status: 'OPEN' }]); fixture.detectChanges(); const c = fixture.componentInstance; c.periodId = id; c.customerId = id; c.precision = '2'; c.midpoint = 'AWAY_FROM_ZERO'; c.draftReference = 'DRAFT-1'; c.documentDate = c.accountingDate = c.supplyDate = '2026-01-10'; c.dueDate = '2026-02-10'; c.lines = [{ accountCode: '4000', description: 'Consulting', quantity: '2', unitPrice: '125.125', discount: '.25' }]; c.lines[0].discount = '0.25'; c.reviewed.set(true); return { fixture, c }; }
  it('keeps the same key/body after loss and requires recovery plus explicit assent before retry', () => {
    const { fixture, c } = setup(); c.save(); const first = http.expectOne(`/api/ui/accounting/clients/${client}/sales-invoice-drafts`); const body = first.request.body; expect(body.lines[0].unitPrice).toBe('125.125'); first.error(new ProgressEvent('lost')); fixture.detectChanges();
    expect(c.pending()).not.toBeNull(); c.sendPending(); http.expectNone(`/api/ui/accounting/clients/${client}/sales-invoice-drafts`);
    c.recover(); http.expectOne(`/api/ui/accounting/clients/${client}/sales-invoice-draft-requests/${body.commandId}`).flush({}, { status: 404, statusText: 'Not found' });
    expect(c.retry()).toBe(true); c.sendPending(); http.expectNone(`/api/ui/accounting/clients/${client}/sales-invoice-drafts`);
    c.reviewed.set(true); c.sendPending(); const repeated = http.expectOne(`/api/ui/accounting/clients/${client}/sales-invoice-drafts`); expect(repeated.request.body).toEqual(body); repeated.flush({ ...draft, commandId: body.commandId }); expect(c.pending()).toBeNull(); expect(c.saved()?.snapshot.gross).toBe('250.00');
  });
  it('clears cross-client pending data and does not accept a cancelled old context', () => {
    const { fixture, c } = setup(); c.save(); const old = http.expectOne(`/api/ui/accounting/clients/${client}/sales-invoice-drafts`); fixture.componentRef.setInput('clientId', id); fixture.detectChanges(); expect(old.cancelled).toBe(true); expect(c.pending()).toBeNull(); expect(c.saved()).toBeNull(); expect(c.reviewed()).toBe(false);
  });
});
