import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionService } from '../../core/session';
import { FinancialPackageBuild } from './package-build';

const id = (n: number) => `00000000-0000-4000-8000-${n.toString().padStart(12, '0')}`;

describe('financial package build intake', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [FinancialPackageBuild], providers: [
      provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
    ] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId: id(1), firmId: id(2), generation: '1', staff: true });
  });
  afterEach(() => { http.verify(); TestBed.resetTestingModule(); });

  it('refuses an empty movement without calling the API', async () => {
    const fixture = TestBed.createComponent(FinancialPackageBuild);
    fixture.detectChanges();
    await fixture.componentInstance.submit();
    expect(fixture.componentInstance.message()).toContain('at least one accepted GL contribution');
    http.expectNone('/api/ui/accounting/packages/build');
  });

  it('uses the blank allocation row when a preparer selects an accepted source line', () => {
    const fixture = TestBed.createComponent(FinancialPackageBuild);
    const page = fixture.componentInstance;
    page.addAcceptedSource(id(5), '100');
    expect(page.movements[0].allocations).toEqual([{ lineId: id(5), amount: '100' }]);
  });

  it('queues exact source-linked supplementary amounts and displays the durable operation', async () => {
    const fixture = TestBed.createComponent(FinancialPackageBuild);
    const page = fixture.componentInstance;
    page.planId = id(3); page.mappingId = id(4); page.periodStart = '2026-01-01'; page.periodEnd = '2026-12-31';
    page.templateVersion = 'template-v1'; page.cashBeginning = '0.00'; page.cashEnding = '100.00';
    page.cashAccountCodes = '1000'; page.openingEvidence = 'opening bank statement'; page.closingEvidence = 'closing bank statement';
    page.movements = [{ section: 'OPERATING', description: 'Receipt', amount: '100.00', isNonCash: false,
      movementDate: '2026-06-30', activityCode: 'CASH_RECEIPTS', sourceReferences: 'GL 2026-06', rationale: 'Reviewed source classification',
      allocations: [{ lineId: id(5), amount: '100.00' }] }];
    page.disclosures = [{ code: 'CASH_POLICY', response: 'Policy evidence', notApplicable: false, rationale: '' }];
    page.equityLines = [{ lineCode: 'RETAINED_EARNINGS', description: 'Retained earnings', opening: '0.00', profitOrLoss: '100.00',
      oci: '0.00', capital: '0.00', dividends: '0.00', closing: '100.00', evidence: 'Equity schedule' }];
    fixture.detectChanges();
    const submitted = page.submit();
    const request = http.expectOne('/api/ui/accounting/packages/build');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.supplementaryInformation.cashFlowLines[0].sourceAllocations).toEqual([
      { generalLedgerLineId: id(5), signedAmount: '100.00' },
    ]);
    expect(request.request.body.supplementaryInformation.cashFlowLines[0].amount).toBe('100.00');
    request.flush({ value: id(6) });
    await submitted;
    fixture.detectChanges();
    expect(page.operationId()).toBe(id(6));
    http.expectOne(`/api/ui/accounting/package-builds/${id(6)}`).flush({
      id: id(6), status: 'PENDING', packageId: null, errorCode: null,
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('PENDING');
  });
});
