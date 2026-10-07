import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ClientOperationalOpeningBalances, decodeOpeningBalance } from './operational-opening-balances';
import { SessionService } from '../../core/session';

const client = '11111111-1111-4111-8111-111111111111';
const periodId = '22222222-2222-4222-8222-222222222222';
const chart = '33333333-3333-4333-8333-333333333333';
const maker = '44444444-4444-4444-8444-444444444444';
const reviewer = '55555555-5555-4555-8555-555555555555';
const openingId = '66666666-6666-4666-8666-666666666666';
const period = { id: periodId, revision: '8', code: 'FY2026', start: '2026-01-01', end: '2026-12-31', currency: 'QAR', status: 'OPEN' };
const response = (approvedByUserId: string | null = null) => ({ id: openingId, clientId: client, periodId, chartVersionId: chart,
  periodRevision: '8', asOfDate: period.start, currency: 'QAR', evidenceReference: 'CUTOVER-TB-2025', evidenceSha256: 'a'.repeat(64),
  manifestSha256: 'b'.repeat(64), createdByUserId: maker, createdAt: '2026-10-07T10:00:00Z', approvedByUserId,
  approvedAt: approvedByUserId ? '2026-10-07T11:00:00Z' : null,
  lines: [{ accountCode: '1000', accountName: 'Cash', debit: '100.000000', credit: '0.000000' },
    { accountCode: '3000', accountName: 'Capital', debit: '0.000000', credit: '100.000000' }] });

describe('Reviewed client opening balances', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [ClientOperationalOpeningBalances], providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); });
  function setup(userId = maker) {
    TestBed.inject(SessionService).current.set({ userId, firmId: client, generation: '1', staff: true });
    const fixture = TestBed.createComponent(ClientOperationalOpeningBalances);
    fixture.componentRef.setInput('clientId', client); fixture.componentRef.setInput('currency', 'QAR'); fixture.componentRef.setInput('periods', [period]); fixture.detectChanges();
    const select = fixture.nativeElement.querySelector('#opening-period') as HTMLSelectElement;
    select.value = periodId; select.dispatchEvent(new Event('change')); fixture.detectChanges();
    http.expectOne(`/api/ui/accounting/clients/${client}/operational-opening-balances/${periodId}`).flush(null);
    return fixture;
  }

  it('validates a balanced client-period manifest and rejects tampered context', () => {
    expect(decodeOpeningBalance(response(), client, period)?.manifestSha256).toBe('b'.repeat(64));
    expect(() => decodeOpeningBalance({ ...response(), clientId: periodId }, client, period)).toThrow();
    expect(() => decodeOpeningBalance({ ...response(), periodRevision: '7' }, client, period)).not.toThrow();
    expect(() => decodeOpeningBalance({ ...response(), lines: [{ ...response().lines[0], credit: '1.000000' }, response().lines[1]] }, client, period)).toThrow();
    expect(() => decodeOpeningBalance({ ...response(), manifestSha256: 'bad' }, client, period)).toThrow();
  });

  it('creates from the exact period revision and recovers the immutable manifest by reading it back', () => {
    const fixture = setup(); const c = fixture.componentInstance;
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('table tbody tr').length).toBe(2);
    expect(fixture.nativeElement.querySelector("input[aria-label='Opening account code row 1']")).toBeTruthy();
    c.evidenceReference = 'CUTOVER-TB-2025'; c.evidenceSha256 = 'a'.repeat(64);
    c.lines.set([{ accountCode: '1000', debit: '100', credit: '0' }, { accountCode: '3000', debit: '0', credit: '100' }]); c.reviewed.set(true);
    expect(c.balanced()).toBe(true); c.create();
    const create = http.expectOne({ method: 'POST', url: `/api/ui/accounting/clients/${client}/operational-opening-balances` });
    expect(create.request.body).toMatchObject({ periodId, periodRevision: '8', asOfDate: period.start, currency: 'QAR', reviewed: true });
    create.flush({ id: openingId });
    const read = http.expectOne(`/api/ui/accounting/clients/${client}/operational-opening-balances/${periodId}`);
    read.flush(response()); fixture.detectChanges();
    expect(c.opening()?.manifestSha256).toBe('b'.repeat(64));
    expect(fixture.nativeElement.textContent).toContain('Awaiting independent review');
    const buttons = fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>;
    expect(Array.from(buttons).some(b => b.textContent?.includes('Approve opening snapshot'))).toBe(false);
  });

  it('lets a different reviewer approve the exact manifest and then displays retained approval evidence', () => {
    const fixture = setup(reviewer); const c = fixture.componentInstance;
    c.opening.set(decodeOpeningBalance(response(), client, period)); fixture.detectChanges();
    c.approvalReviewed.set(true); c.approve(c.opening()!);
    const approve = http.expectOne({ method: 'POST', url: `/api/ui/accounting/clients/${client}/operational-opening-balances/${openingId}/approve` });
    expect(approve.request.body).toEqual({ periodRevision: '8', manifestSha256: 'b'.repeat(64), reviewed: true });
    approve.flush(null);
    http.expectOne(`/api/ui/accounting/clients/${client}/operational-opening-balances/${periodId}`).flush(response(reviewer)); fixture.detectChanges();
    expect(c.opening()?.approvedByUserId).toBe(reviewer);
    expect(fixture.nativeElement.textContent).toContain('Independently approved');
  });
});
