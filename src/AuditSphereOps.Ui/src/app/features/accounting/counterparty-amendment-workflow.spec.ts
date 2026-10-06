import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CounterpartyHistoryWorkspace } from './counterparty-history';
import { SessionService } from '../../core/session';
const client = '11111111-1111-4111-8111-111111111111';
const party = '22222222-2222-4222-8222-222222222222';
const current = { id: party, clientId: client, legalName: 'Example', displayName: 'Example', role: 'BOTH', address: 'Original', country: 'QA', taxIdentifier: '', contactDetails: '', paymentTerms: '', defaultCurrency: '', externalSystem: '', externalReference: '', createdByUserId: party, createdAt: '2026-10-06T00:00:00Z', revision: '1', effectiveAmendmentId: null };
const history = { current, bookkeepingActive: true, page: 0, pageSize: 25, total: 0, effectiveAmendment: null, amendments: [] };
const url = `/api/ui/accounting/clients/${client}/counterparties/${party}`;
describe('Counterparty amendment outcome recovery', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [CounterpartyHistoryWorkspace], providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: party, firmId: client, generation: '1', staff: true });
  });
  afterEach(() => { http.verify(); TestBed.resetTestingModule(); });
  it('requires fresh history and an explicit reset after an unknown proposal outcome', () => {
    const fixture = TestBed.createComponent(CounterpartyHistoryWorkspace);
    fixture.componentRef.setInput('clientId', client); fixture.componentRef.setInput('partyId', party); fixture.detectChanges();
    http.expectOne(r => r.url === url && r.params.get('page') === '0').flush(history); fixture.detectChanges();
    const component = fixture.componentInstance;
    component.draft.reason = 'Synthetic update'; component.draft.address = 'New address'; component.reviewed.set(true); component.propose();
    http.expectOne(url + '/amendments').flush(null); fixture.detectChanges();
    expect(component.unknown()).toBe(true); expect(component.history()).toBeNull();
    component.reviewed.set(true); component.propose(); http.expectNone(url + '/amendments');
    const reset = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>).find(b => b.textContent?.includes('I inspected history'))!;
    expect(reset.disabled).toBe(true);
    component.refresh(); http.expectOne(r => r.url === url).flush(history); fixture.detectChanges();
    expect(component.unknown()).toBe(true); expect(reset.disabled).toBe(false);
    reset.click(); fixture.detectChanges(); expect(component.unknown()).toBe(false); expect(component.reviewed()).toBe(false);
  });
  it('clears the session on an unauthorized read without repeatedly requesting history', () => {
    const fixture = TestBed.createComponent(CounterpartyHistoryWorkspace);
    fixture.componentRef.setInput('clientId', client); fixture.componentRef.setInput('partyId', party); fixture.detectChanges();
    http.expectOne(r => r.url === url).flush({}, { status: 401, statusText: 'Unauthorized' }); fixture.detectChanges();
    expect(TestBed.inject(SessionService).current()).toBeNull(); expect(fixture.componentInstance.history()).toBeNull();
    http.expectNone(r => r.url === url);
  });
});
