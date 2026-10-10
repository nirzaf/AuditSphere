import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { decode } from '../../core/decode';
import { EndOfServiceProvision, decodeEndOfService, validAccrualAmount } from './end-of-service';

const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const other = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const treatment = {
  id, version: 1, status: 'CONFIRMED', measurementTreatment: 'Measured per the firm reporting framework.',
  accountantName: 'N. Accountant', accountantCredential: 'CPA 1', provisionAccount: '2400 · Provision',
  expenseAccount: '6150 · Expense', recordedBy: 'Finance', recordedAt: '2026-10-10T09:00:00+00:00',
  confirmedBy: 'Partner', confirmedAt: '2026-10-10T10:00:00+00:00', confirmationNote: 'Confirmed',
};
const workspace = {
  canRecordTreatment: true, canConfirmTreatment: false, canPrepareAccrual: true, accrualBlockedReason: null, treatment,
  treatmentVersion: 1, currency: 'QAR', postedProvisionBalance: '12500.00',
  liabilityAccounts: [{ id, code: '2400', name: 'Provision' }], expenseAccounts: [{ id: other, code: '6150', name: 'Expense' }],
  openPeriods: [{ id, periodCode: '2026-10' }], accruals: [], maxEvidenceBytes: 5242880,
};
const blocked = { ...workspace, canPrepareAccrual: false, accrualBlockedReason: 'Blocked until a Partner confirms the treatment.',
  treatment: { ...treatment, status: 'RECORDED', confirmedBy: null, confirmedAt: null, confirmationNote: null } };

describe('End-of-service provision contract', () => {
  it('keeps exact amounts and the server-computed gates', () => {
    const w = decode(decodeEndOfService, workspace);
    expect([w.postedProvisionBalance, w.canPrepareAccrual, w.treatment?.status]).toEqual(['12500.00', true, 'CONFIRMED']);
  });
  it('rejects numeric money, unknown treatment states and missing gates', () => {
    expect(() => decode(decodeEndOfService, { ...workspace, postedProvisionBalance: 12500 })).toThrow();
    expect(() => decode(decodeEndOfService, { ...workspace, treatment: { ...treatment, status: 'APPROVED' } })).toThrow();
    expect(() => decode(decodeEndOfService, { ...workspace, canPrepareAccrual: undefined })).toThrow();
  });
  it('accepts only a positive amount with at most two decimals', () => {
    expect(['12500', '12500.5', '0.01'].every(validAccrualAmount)).toBe(true);
    expect(['0', '0.00', '-5', '10.005', '1e3', '', ' 5'].some(validAccrualAmount)).toBe(false);
  });
});

describe('End-of-service provision workbench', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [EndOfServiceProvision], providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(SessionService).current.set({ userId: id, firmId: id, generation: '0', staff: true });
  });
  afterEach(() => { TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); });

  it('shows the block and offers no accrual form until the treatment is confirmed', () => {
    const f = TestBed.createComponent(EndOfServiceProvision), http = TestBed.inject(HttpTestingController);
    f.detectChanges(); TestBed.tick();
    http.expectOne('/api/ui/finance/end-of-service').flush(blocked);
    f.detectChanges();
    const page = f.nativeElement as HTMLElement;
    expect(page.textContent).toContain('Blocked until a Partner confirms the treatment.');
    expect(page.querySelector('#accrual-amount')).toBeNull();
    expect(page.querySelector('#confirmation-note')).toBeNull();
    f.destroy();
  });

  it('sends the entered basis once and never without the reviewed confirmation', async () => {
    const f = TestBed.createComponent(EndOfServiceProvision), http = TestBed.inject(HttpTestingController);
    f.detectChanges(); TestBed.tick();
    http.expectOne('/api/ui/finance/end-of-service').flush(workspace);
    f.detectChanges();
    const c = f.componentInstance;
    c.periodId = id; c.amount = '12500.00'; c.method = 'One month of gratuity'; c.inputs = '42 staff; October payroll';
    c.calculationDate = '2026-10-01'; c.reason = 'Monthly accrual';
    await c.prepare(5242880);
    http.expectNone('/api/ui/finance/end-of-service/accruals');
    c.accrualReviewed = true;
    const sent = c.prepare(5242880);
    const request = http.expectOne('/api/ui/finance/end-of-service/accruals');
    const form = request.request.body as FormData;
    expect([form.get('amount'), form.get('method'), form.get('calculationDate'), form.get('reviewed')])
      .toEqual(['12500.00', 'One month of gratuity', '2026-10-01', 'true']);
    expect(String(form.get('requestId'))).toMatch(/^[0-9a-f-]{36}$/);
    request.flush({ value: id });
    await sent;
    http.expectOne('/api/ui/finance/end-of-service').flush(workspace);
    expect([c.amount, c.accrualReviewed]).toEqual(['', false]);
    expect(c.message()).toContain('draft journal');
    f.destroy();
  });
});
