import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { SessionService } from '../../core/session';
import { ClientFinancialPackage, saveClientDecisionReference } from './package';

const packageId = '11111111-1111-4111-8111-111111111111';
const userId = '22222222-2222-4222-8222-222222222222';
const firmId = '33333333-3333-4333-8333-333333333333';
const requestId = '44444444-4444-4444-8444-444444444444';
const decisionId = '55555555-5555-4555-8555-555555555555';
const hash = 'a'.repeat(64);

const packageView = {
  packageId, framework: 'IFRS', periodStart: '2025-01-01', periodEnd: '2025-12-31', currency: 'QAR',
  templateVersion: 'ifrs-v1', packageHash: hash, hasCashFlow: true, hasDisclosures: true,
  managementDecision: 'PENDING', managementDecidedAt: null, statementTotals: [], composition: null, latestDecisionId: null,
};

describe('client package decision receipt recovery', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'portal/accounting/packages/:id', component: ClientFinancialPackage }]),
        provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).current.set({ userId, firmId, generation: '1', staff: false });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => {
    try { http.verify({ ignoreCancelled: true }); }
    finally { sessionStorage.clear(); TestBed.resetTestingModule(); }
  });

  async function open(): Promise<ClientFinancialPackage> {
    const component = await harness.navigateByUrl(`/portal/accounting/packages/${packageId}`, ClientFinancialPackage);
    http.expectOne(`/api/ui/portal/accounting/packages/${packageId}`).flush(packageView);
    await harness.fixture.whenStable();
    return component;
  }

  it('recovers a saved decision after reload by looking up the exact request receipt', async () => {
    saveClientDecisionReference(sessionStorage, { firmId, userId, generation: '1', packageId }, requestId);
    await open();
    expect(harness.routeNativeElement?.textContent).toContain('previous decision request may have been saved');

    const button = Array.from(harness.routeNativeElement!.querySelectorAll('button')).find(x => x.textContent?.includes('Check saved decision'))!;
    button.click();
    http.expectOne(`/api/ui/portal/accounting/packages/${packageId}/decision-receipts/${requestId}`).flush({
      requestId, found: true, decisionId, decision: 'APPROVED', packageHash: hash, presentationRevisionId: null,
      presentationCompositionId: null, compositionManifestDigest: null, decidedAt: '2026-10-15T11:00:00Z',
    });
    await harness.fixture.whenStable();

    expect(harness.routeNativeElement?.textContent).toContain('Saved APPROVED decision confirmed');
    expect(sessionStorage.length).toBe(0);
    http.expectOne(`/api/ui/portal/accounting/packages/${packageId}`).flush({ ...packageView, managementDecision: 'APPROVED', latestDecisionId: decisionId });
    await harness.fixture.whenStable();
  });

  it('requires a receipt check and explicit exact-intent confirmation before retrying the same request ID', async () => {
    saveClientDecisionReference(sessionStorage, { firmId, userId, generation: '1', packageId }, requestId);
    const component = await open();

    const check = Array.from(harness.routeNativeElement!.querySelectorAll('button')).find(x => x.textContent?.includes('Check saved decision'))!;
    check.click();
    http.expectOne(`/api/ui/portal/accounting/packages/${packageId}/decision-receipts/${requestId}`).flush({
      requestId, found: false, decisionId: null, decision: null, packageHash: null, presentationRevisionId: null,
      presentationCompositionId: null, compositionManifestDigest: null, decidedAt: null,
    });
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement?.textContent).toContain('No saved receipt was visible at this check');

    component.form.patchValue({ evidenceReference: 'Board minute 4', reviewed: true });
    await harness.fixture.whenStable();
    const confirmation = harness.routeNativeElement!.querySelector('input[type="checkbox"]') as HTMLInputElement;
    confirmation.checked = true; confirmation.dispatchEvent(new Event('change'));
    await harness.fixture.whenStable();
    const retry = Array.from(harness.routeNativeElement!.querySelectorAll('button')).find(x => x.textContent?.includes('Retry the original request'))!;
    expect(retry.disabled).toBe(false);
    retry.click();
    const post = http.expectOne({ method: 'POST', url: `/api/ui/portal/accounting/packages/${packageId}/decision` });
    expect(post.request.body.requestId).toBe(requestId);
    expect(post.request.body.expectedHash).toBe(hash);
    expect(post.request.body.evidenceReference).toBe('Board minute 4');
    post.flush({ value: decisionId });
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement?.textContent).toContain('Management decision recorded for this exact package');
    expect(sessionStorage.length).toBe(0);
    http.expectOne(`/api/ui/portal/accounting/packages/${packageId}`).flush({ ...packageView, managementDecision: 'APPROVED', latestDecisionId: decisionId });
    await harness.fixture.whenStable();
  });
});
