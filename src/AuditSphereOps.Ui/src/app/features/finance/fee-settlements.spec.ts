import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { routes } from '../../app.routes';
import { NAVIGATION } from '../../navigation';
import { SessionService } from '../../core/session';
import { FeeAgreementDetailRoute, FeeSettlements, decodeFeeAgreementQueuePage } from './fee-settlements';

const userId = '11111111-1111-4111-8111-111111111111';
const firmId = '22222222-2222-4222-8222-222222222222';
const agreementId = '33333333-3333-4333-8333-333333333333';
const clientId = '44444444-4444-4444-8444-444444444444';
const otherAgreementId = '55555555-5555-4555-8555-555555555555';
const base = '/api/ui/finance/fee-agreements';
const row = {
  agreementId,
  clientId,
  clientName: 'Synthetic client',
  fee: '10000.00',
  currency: 'QAR',
  createdAt: '2026-10-18T11:00:00Z',
};
const page = (value: number, hasMore: boolean, rows: unknown[] = [row]) => ({ page: value, hasMore, rows });

describe('Finance fee settlements', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'app/finance/fee-agreements', component: FeeSettlements },
          { path: 'app/finance/fee-agreements/:id', component: FeeAgreementDetailRoute },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    TestBed.inject(SessionService).current.set({ userId, firmId, generation: '1', staff: true });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => {
    try { http.verify({ ignoreCancelled: true }); }
    finally { TestBed.resetTestingModule(); }
  });

  it('decodes bounded rows with exact money and rejects malformed identities or page payloads', () => {
    expect(decodeFeeAgreementQueuePage(page(0, false))).toEqual(page(0, false));
    expect(() => decodeFeeAgreementQueuePage(page(0, false, Array(21).fill(row)))).toThrow();
    expect(() => decodeFeeAgreementQueuePage(page(0, false, [{ ...row, fee: 10000 }]))).toThrow();
    expect(() => decodeFeeAgreementQueuePage(page(0, false, [{ ...row, agreementId: 'invalid' }]))).toThrow();
    expect(() => decodeFeeAgreementQueuePage({ ...page(0, false), page: -1 })).toThrow();
  });

  it('shows client agreements with native detail and page links, then opens the exact finance detail route', async () => {
    const list = await harness.navigateByUrl('/app/finance/fee-agreements', FeeSettlements);
    http.expectOne(`${base}?page=0`).flush(page(0, true));
    await harness.fixture.whenStable();

    const host = harness.routeNativeElement!;
    expect(host.querySelector('h1')?.textContent).toContain('Fee settlements');
    expect(host.querySelector('table caption')?.textContent).toBe('Fee agreements and settlement status');
    expect(host.querySelector('tbody th[scope="row"]')?.textContent).toContain('Synthetic client');
    const detailLink = host.querySelector<HTMLAnchorElement>('tbody a[href]');
    expect(detailLink?.getAttribute('href')).toBe(`/app/finance/fee-agreements/${agreementId}`);
    const nextLink = [...host.querySelectorAll<HTMLAnchorElement>('a')]
      .find((link) => link.textContent?.trim() === 'Next page');
    expect(nextLink?.getAttribute('href')).toBe('/app/finance/fee-agreements?page=1');
    expect(host.querySelector('button')?.textContent).toContain('Refresh fee settlements');

    await harness.navigateByUrl(nextLink!.getAttribute('href')!, FeeSettlements);
    http.expectOne(`${base}?page=1`).flush(page(1, false, []));
    await harness.fixture.whenStable();
    const pageOneHost = harness.routeNativeElement!;
    expect(pageOneHost.querySelector('[aria-current="page"]')?.textContent).toContain('Page 2');
    expect([...pageOneHost.querySelectorAll('a')].some((link) => link.textContent?.trim() === 'Next page')).toBe(false);
    const previousLink = [...pageOneHost.querySelectorAll<HTMLAnchorElement>('a')]
      .find((link) => link.textContent?.trim() === 'Previous page');
    expect(previousLink?.getAttribute('href')).toBe('/app/finance/fee-agreements?page=0');

    const detail = await harness.navigateByUrl(detailLink!.getAttribute('href')!, FeeAgreementDetailRoute);
    expect(detail).toBeTruthy();
    http.expectOne(`${base}/${agreementId}`).flush({
      proposalId: otherAgreementId,
      agreementId,
      clientId,
      clientName: 'Synthetic client',
      engagementId: null,
      fee: '10000.00',
      currency: 'QAR',
      advancePercent: '50',
      canCreate: false,
      canFinance: true,
      releaseRecorded: false,
      creationBlockers: [],
      milestones: [
        { id: '66666666-6666-4666-8666-666666666666', kind: 'ADVANCE', amount: '5000.00', state: 'PENDING', invoiceId: null, invoiceNumber: null, invoiceStatus: null, allocated: '0', outstanding: '5000.00', paidAt: null },
        { id: '77777777-7777-4777-8777-777777777777', kind: 'BALANCE', amount: '5000.00', state: 'PENDING', invoiceId: null, invoiceNumber: null, invoiceStatus: null, allocated: '0', outstanding: '5000.00', paidAt: null },
      ],
      engagements: [],
      advancePreparation: { state: 'PENDING_AUTOMATION_DISABLED', message: 'Finance prepares the draft manually.' },
    });
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain('Client fee settlement');
    expect(harness.routeNativeElement?.textContent).toContain('Synthetic client');
    expect(harness.routeNativeElement?.querySelector('h2')?.textContent).toContain('Fee settlement');
    expect(harness.routeNativeElement?.querySelector('tbody tr')?.textContent).toContain('ADVANCE');
    expect(list).toBeTruthy();
  });

  it('removes listed agreements when the current session is invalidated', async () => {
    await harness.navigateByUrl('/app/finance/fee-agreements', FeeSettlements);
    http.expectOne(`${base}?page=0`).flush(page(0, false));
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement?.textContent).toContain('Synthetic client');

    TestBed.inject(SessionService).clear();
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement?.textContent).not.toContain('Synthetic client');
    expect(harness.routeNativeElement?.querySelector('tbody tr')).toBeNull();
  });

  it('registers both finance routes and exposes the queue in the existing Practice navigation', () => {
    expect(routes.some((route) => route.path === 'app/finance/fee-agreements')).toBe(true);
    expect(routes.some((route) => route.path === 'app/finance/fee-agreements/:id')).toBe(true);
    expect(NAVIGATION.flatMap((group) => group.items).some((item) =>
      item.label === 'Fee settlements' && item.path === '/app/finance/fee-agreements')).toBe(true);
  });
});
