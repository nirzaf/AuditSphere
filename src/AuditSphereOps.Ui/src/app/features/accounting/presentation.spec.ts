import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { StatementPresentation, decodePresentation, longDate, presentationAmount } from './presentation';

const id = '11111111-1111-4111-8111-111111111111';
const hash64 = 'a'.repeat(64);
const row = (overrides: Record<string, unknown> = {}) => ({
  kind: 'LINE', code: 'CASH', label: 'Cash', depth: 1, noteReference: '7', current: '1090993', prior: '58372', ...overrides,
});
const payload = {
  status: 'UNAPPROVED_DRAFT',
  displayDecimals: 0,
  header: { entity: 'SYNTHETIC CLIENT', framework: 'IFRS', currency: 'QAR', currentPeriodEnd: '2025-12-31', priorPeriodEnd: '2024-12-31', reference: 'FSP-SYN' },
  manifest: {
    packageId: id, calculationHash: hash64, layoutVersionId: id, layoutHash: hash64,
    comparativePackageId: null, comparativeCalculationHash: null, builderVersion: 'auditsphere.statement-presentation.v4', displayPolicy: 'whole units',
  },
  statements: [
    { code: 'FINANCIAL_POSITION', title: 'Statement of financial position', rows: [row(), row({ kind: 'HEADING', code: 'H', label: 'Assets', noteReference: null, current: null, prior: null })] },
    { code: 'CHANGES_IN_EQUITY', title: 'Statement of changes in equity', rows: [row({ code: 'EQ1', label: 'Closing balance', noteReference: null, current: null, prior: null })] },
    { code: 'CASH_FLOWS', title: 'Statement of cash flows', rows: [row({ code: 'CF1', label: 'Net change in cash', noteReference: null, current: null, prior: null })] },
  ],
  checks: [
    { code: 'TB_BALANCED', passed: true, detail: 'The current balances net to zero.' },
    { code: 'CASH_FLOW_SOURCE_LINEAGE', passed: false, detail: 'Movement references are not source-resolved.' },
  ],
  warnings: [{ code: 'CASH_FLOW_LINEAGE_PENDING', detail: 'Cash-flow evidence is not source-verified.' }],
};

describe('Statement presentation contract', () => {
  it('keeps exact amounts as strings and the draft status', () => {
    const decoded = decodePresentation(payload, 'presentation');
    expect(decoded.statements[0].rows[0].current).toBe('1090993');
    expect(decoded.status).toBe('UNAPPROVED_DRAFT');
  });

  it('rejects numeric money, unknown statuses and missing manifests', () => {
    expect(() => decodePresentation({ ...payload, statements: [{ code: 'X', title: 'X', rows: [row({ current: 1090993 })] }] }, 'p')).toThrow();
    expect(() => decodePresentation({ ...payload, status: 'APPROVED' }, 'p')).toThrow();
    expect(() => decodePresentation({ ...payload, manifest: undefined }, 'p')).toThrow();
  });

  it('formats amounts as the server text does: grouped, brackets for negatives, dash for zero', () => {
    expect(presentationAmount('3818172')).toBe('3,818,172');
    expect(presentationAmount('-508247')).toBe('(508,247)');
    expect(presentationAmount('0')).toBe('–');
    expect(presentationAmount(null)).toBe('Not available');
    expect(presentationAmount('1234.5', 2)).toBe('1,234.50');
    expect(longDate('2025-12-31')).toBe('31 December 2025');
  });
});

describe('Statement presentation screen', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  it('shows the draft banner, the checks and grouped figures, and exports the displayed layout version', () => {
    TestBed.configureTestingModule({ imports: [StatementPresentation], providers: [provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(SessionService).current.set({ userId: id, firmId: id, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(StatementPresentation);
    fixture.componentRef.setInput('packageId', id);
    fixture.detectChanges();
    http.expectOne(`/api/ui/accounting/packages/${id}/presentation`).flush(payload);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Unapproved presentation draft');
    expect(host.textContent).toContain('1,090,993');
    expect(host.textContent).toContain('As at 31 December 2025');
    expect(host.textContent).toContain('Statement of changes in equity');
    expect(host.textContent).toContain('Statement of cash flows');
    expect(host.textContent).toContain('Year ended 31 December 2025');
    expect(host.textContent).toContain('2 checks · 1 failed');
    expect(host.textContent).toContain('Readiness and rounding notes');
    expect(host.querySelector('tr.heading')).not.toBeNull();
    fixture.componentInstance.download('xlsx');
    const exported = http.expectOne(`/api/ui/accounting/packages/${id}/presentation/export`);
    expect(exported.request.body).toEqual({ format: 'xlsx', layoutVersionId: id });
    exported.flush(new Blob(['x']));
    fixture.destroy();
  });
});
