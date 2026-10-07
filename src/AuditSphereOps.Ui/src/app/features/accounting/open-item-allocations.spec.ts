import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OpenItemAllocations } from './open-item-allocations';

const client = '11111111-1111-4111-8111-111111111111';
const period = '22222222-2222-4222-8222-222222222222';

describe('client open-item control reconciliation', () => {
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
