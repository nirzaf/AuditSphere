import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SessionService } from '../../core/session';
import { EqrDecision } from './eqr-decision';

const engagementId = '11111111-1111-4111-8111-111111111111';
const reportId = '22222222-2222-4222-8222-222222222222';
const decisionId = '33333333-3333-4333-8333-333333333333';
const factsA = 'a'.repeat(64);
const reportHashA = 'b'.repeat(64);
const basisA = { caseRevision: 4, factsDigest: factsA, reportDeliverableId: reportId, reportSha256: reportHashA };
const basisB = { ...basisA, caseRevision: 5, factsDigest: 'c'.repeat(64), reportSha256: 'd'.repeat(64) };
const basisUrl = `/api/ui/engagements/${engagementId}/completion/eqr-review-basis`;
const decisionUrl = `/api/ui/engagements/${engagementId}/completion/eqr-decision`;
const firstSession = { userId: '44444444-4444-4444-8444-444444444444', firmId: '55555555-5555-4555-8555-555555555555', generation: '7', staff: true };

describe('native EQR decision panel', () => {
  function setup() {
    TestBed.configureTestingModule({
      imports: [EqrDecision],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const session = TestBed.inject(SessionService);
    session.current.set(firstSession);
    const fixture = TestBed.createComponent(EqrDecision);
    fixture.componentRef.setInput('engagementId', engagementId);
    fixture.componentRef.setInput('caseStatus', 'IN_PROGRESS');
    fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, http: TestBed.inject(HttpTestingController), session };
  }

  async function loadBasis(http: HttpTestingController, fixture: ReturnType<typeof setup>['fixture'], value = basisA): Promise<void> {
    http.expectOne(basisUrl).flush(value);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function enterNewDecision(fixture: ReturnType<typeof setup>['fixture'], status: string, notes: string, findings: boolean): void {
    const host = fixture.nativeElement as HTMLElement;
    const select = host.querySelector('#eqr-decision-status') as HTMLSelectElement;
    select.value = status;
    select.dispatchEvent(new Event('change', { bubbles: true }));
    const noteField = host.querySelector('#eqr-decision-notes') as HTMLTextAreaElement;
    noteField.value = notes;
    noteField.dispatchEvent(new Event('input', { bubbles: true }));
    const findingsField = host.querySelector('input[name="eqr-findings-discussed"]') as HTMLInputElement;
    findingsField.checked = findings;
    findingsField.dispatchEvent(new Event('change', { bubbles: true }));
  }

  function reviewExactBasis(fixture: ReturnType<typeof setup>['fixture']): void {
    const field = (fixture.nativeElement as HTMLElement).querySelector('input[name="eqr-exact-basis-reviewed"]') as HTMLInputElement;
    field.checked = true;
    field.dispatchEvent(new Event('change', { bubbles: true }));
  }

  async function waitForRequest(http: HttpTestingController, url: string): Promise<TestRequest> {
    let request: TestRequest | undefined;
    await vi.waitFor(() => {
      const matches = http.match(url);
      if (matches.length) request = matches[0];
      expect(request).toBeDefined();
    });
    return request!;
  }

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
    sessionStorage.clear();
  });

  it('requires explicit review and submits the exact current report basis once', async () => {
    const { fixture, page, http } = setup();
    await loadBasis(http, fixture);
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain(reportId);
    expect(host.textContent).toContain(reportHashA);
    expect((host.querySelector('a[href="/api/deliverables/' + reportId + '/download"]') as HTMLAnchorElement)?.rel).toContain('noopener');

    enterNewDecision(fixture, 'CONCURRED', 'Reviewed the exact current report and discussed all findings.', true);
    fixture.detectChanges();
    expect((host.querySelector('[data-eqr-submit]') as HTMLButtonElement).disabled).toBe(true);
    http.expectNone(decisionUrl);

    reviewExactBasis(fixture);
    await fixture.whenStable();
    fixture.detectChanges();
    expect((host.querySelector('[data-eqr-submit]') as HTMLButtonElement).disabled).toBe(false);
    (host.querySelector('[data-eqr-submit]') as HTMLButtonElement).click();
    const post = await waitForRequest(http, decisionUrl);
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toMatchObject({
      status: 'CONCURRED', findingsDiscussed: true, expectedCaseRevision: 4,
      expectedFactsDigest: factsA, expectedReportDeliverableId: reportId, expectedReportSha256: reportHashA,
    });
    const requestKey = post.request.body.requestKey as string;
    expect(requestKey).toMatch(/^[0-9a-f-]{36}$/i);
    const saved = JSON.parse(sessionStorage.getItem(`auditsphere-eqr-pending-v1:${firstSession.firmId}:${firstSession.userId}:${firstSession.generation}:${engagementId}`)!);
    expect(JSON.stringify(saved)).not.toContain('Reviewed the exact current report');
    post.flush({ value: decisionId });
    await vi.waitFor(() => expect(page.busy()).toBe(false));
    expect(page.message()).toContain(decisionId);
    http.expectOne(basisUrl).flush(basisA);
    await fixture.whenStable();
    expect(sessionStorage.length).toBe(0);
  });

  it('recovers an unknown outcome with the same request, basis and content without storing notes', async () => {
    const first = setup();
    await loadBasis(first.http, first.fixture);
    const notes = 'The inventory valuation evidence requires a documented correction.';
    enterNewDecision(first.fixture, 'CHANGES_REQUESTED', notes, false);
    reviewExactBasis(first.fixture);
    await first.fixture.whenStable();
    first.fixture.detectChanges();
    (first.fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-eqr-submit]')!.click();
    const original = await waitForRequest(first.http, decisionUrl);
    const originalBody = original.request.body;
    const storageKey = `auditsphere-eqr-pending-v1:${firstSession.firmId}:${firstSession.userId}:${firstSession.generation}:${engagementId}`;
    const raw = sessionStorage.getItem(storageKey)!;
    expect(raw).not.toContain(notes);
    original.flush({ code: 'temporarily.unavailable', message: 'try later' }, { status: 503, statusText: 'Unavailable' });
    await vi.waitFor(() => expect(first.page.busy()).toBe(false));
    expect(first.page.pending()?.requestKey).toBe(originalBody.requestKey);
    expect(first.page.error()).toContain('outcome is unconfirmed');

    first.fixture.destroy();
    const resumed = TestBed.createComponent(EqrDecision);
    resumed.componentRef.setInput('engagementId', engagementId);
    resumed.componentRef.setInput('caseStatus', 'RECORDED_CONCURRENCE_NOT_CURRENT');
    resumed.detectChanges();
    await loadBasis(first.http, resumed);
    expect(resumed.componentInstance.pending()?.requestKey).toBe(originalBody.requestKey);
    const host = resumed.nativeElement as HTMLElement;
    const recovery = host.querySelector('#eqr-recovery-notes') as HTMLTextAreaElement;
    recovery.value = notes;
    recovery.dispatchEvent(new Event('input', { bubbles: true }));
    resumed.detectChanges();
    host.querySelector<HTMLButtonElement>('[data-eqr-retry]')!.click();
    const replay = await waitForRequest(first.http, decisionUrl);
    expect(replay.request.body).toEqual(originalBody);
    replay.flush({ value: decisionId });
    await vi.waitFor(() => expect(resumed.componentInstance.busy()).toBe(false));
    expect(resumed.componentInstance.message()).toContain('current concurrence is not assumed');
    first.http.expectOne(basisUrl).flush(basisA);
    await resumed.whenStable();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
  });

  it('keeps the form unavailable when the server denies basis access', async () => {
    const { fixture, http } = setup();
    http.expectOne(basisUrl).flush({ code: 'scope.denied', message: 'restricted details' }, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('unavailable in this scope');
    expect(host.querySelector('[data-eqr-submit]')).toBeNull();
    expect(host.querySelector('[data-eqr-retry]')).toBeNull();
  });

  it('clears decision assent and draft when the reviewed basis or session changes', async () => {
    const { fixture, page, http, session } = setup();
    await loadBasis(http, fixture);
    page.reviewedExactBasis.set(true);
    page.acceptBasis(page.basis.data()!);
    page.status = 'CONCURRED';
    page.notes = 'Draft for the first report';
    page.findingsDiscussed.set(true);
    expect(page.canSubmit(page.basis.data()!)).toBe(true);

    page.basis.reload();
    http.expectOne(basisUrl).flush(basisB);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.reviewedExactBasis()).toBe(false);
    expect(page.notes).toBe('');
    expect(page.canSubmit(page.basis.data()!)).toBe(false);

    page.reviewedExactBasis.set(true);
    page.acceptBasis(page.basis.data()!);
    expect(page.reviewedExactBasis()).toBe(true);
    session.current.set({ ...firstSession, userId: '66666666-6666-4666-8666-666666666666', generation: '8' });
    await fixture.whenStable();
    expect(page.reviewedExactBasis()).toBe(false);
    expect(page.notes).toBe('');
    http.expectOne(basisUrl).flush(basisB);
    await fixture.whenStable();
    expect(page.reviewedExactBasis()).toBe(false);
    expect(page.canSubmit(page.basis.data()!)).toBe(false);
    http.expectNone(decisionUrl);
  });
});
