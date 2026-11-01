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
const storageKey = `auditsphere-eqr-pending-v2:${firstSession.firmId}:${firstSession.userId}:${engagementId}`;
const legacyKey = `auditsphere-eqr-pending-v1:${firstSession.firmId}:${firstSession.userId}:${firstSession.generation}:${engagementId}`;

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

  async function waitForRequest(http: HttpTestingController, url: string, description = url): Promise<TestRequest> {
    let request: TestRequest | undefined;
    await vi.waitFor(() => {
      const matches = http.match(url);
      if (matches.length) request = matches[0];
      expect(request, `Expected ${description}`).toBeDefined();
    });
    return request!;
  }

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
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
    const saved = JSON.parse(sessionStorage.getItem(storageKey)!);
    expect(JSON.stringify(saved)).not.toContain('Reviewed the exact current report');
    post.flush({ value: decisionId });
    await vi.waitFor(() => expect(page.busy()).toBe(false));
    expect(page.message()).toContain(decisionId);
    http.expectOne(basisUrl).flush(basisA);
    await fixture.whenStable();
    expect(sessionStorage.length).toBe(0);
  });

  it.each([false, true])('recovers an unknown outcome across session replacement (legacy key: %s)', async (legacy) => {
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
    const raw = sessionStorage.getItem(storageKey)!;
    expect(raw).not.toContain(notes);
    original.flush({ code: 'temporarily.unavailable', message: 'try later' }, { status: 503, statusText: 'Unavailable' });
    await vi.waitFor(() => expect(first.page.busy()).toBe(false));
    expect(first.page.pending()?.requestKey).toBe(originalBody.requestKey);
    expect(first.page.error()).toContain('outcome is unconfirmed');

    first.fixture.destroy();
    if (legacy) {
      sessionStorage.setItem(legacyKey, raw);
      sessionStorage.removeItem(storageKey);
    }
    first.session.current.set({ ...firstSession, generation: '8' });
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
    expect(sessionStorage.getItem(legacy ? legacyKey : storageKey)).toBeNull();
    resumed.destroy();
  });

  it('recovers multiple legacy references individually before allowing a new decision', async () => {
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
    const originalRaw = sessionStorage.getItem(storageKey)!;
    original.flush({ code: 'temporarily.unavailable', message: 'try later' }, { status: 503, statusText: 'Unavailable' });
    await vi.waitFor(() => expect(first.page.busy()).toBe(false));

    const firstPending = JSON.parse(originalRaw) as Record<string, unknown>;
    const firstLegacyKey = legacyKey;
    const secondGeneration = '6';
    const secondRequestKey = '66666666-6666-4666-8666-666666666666';
    const secondLegacyKey = `auditsphere-eqr-pending-v1:${firstSession.firmId}:${firstSession.userId}:${secondGeneration}:${engagementId}`;
    const secondPending = { ...firstPending, sessionGeneration: secondGeneration, requestKey: secondRequestKey };
    sessionStorage.removeItem(storageKey);
    sessionStorage.setItem(firstLegacyKey, originalRaw);
    sessionStorage.setItem(secondLegacyKey, JSON.stringify(secondPending));

    first.fixture.destroy();
    first.session.current.set({ ...firstSession, generation: '8' });
    const resumed = TestBed.createComponent(EqrDecision);
    resumed.componentRef.setInput('engagementId', engagementId);
    resumed.componentRef.setInput('caseStatus', 'RECORDED_CONCURRENCE_NOT_CURRENT');
    resumed.detectChanges();
    await loadBasis(first.http, resumed);
    const page = resumed.componentInstance;
    const host = resumed.nativeElement as HTMLElement;
    expect(page.recoveryReferences()).toHaveLength(2);
    expect(host.querySelectorAll('[data-eqr-recovery-choice] option')).toHaveLength(3);
    expect(host.querySelector('[data-eqr-submit]')).toBeNull();
    first.http.expectNone(decisionUrl);

    const choice = host.querySelector<HTMLSelectElement>('[data-eqr-recovery-choice]')!;
    choice.value = firstLegacyKey;
    choice.dispatchEvent(new Event('change', { bubbles: true }));
    resumed.detectChanges();
    host.querySelector<HTMLButtonElement>('[data-eqr-select-recovery]')!.click();
    resumed.detectChanges();
    expect(page.pending()?.requestKey).toBe(originalBody.requestKey);
    const recoveryNotes = host.querySelector<HTMLTextAreaElement>('#eqr-recovery-notes')!;
    recoveryNotes.value = notes;
    recoveryNotes.dispatchEvent(new Event('input', { bubbles: true }));
    resumed.detectChanges();
    host.querySelector<HTMLButtonElement>('[data-eqr-retry]')!.click();
    const firstReplay = await waitForRequest(first.http, decisionUrl);
    expect(firstReplay.request.body).toEqual(originalBody);
    firstReplay.flush({ value: decisionId });
    await vi.waitFor(() => expect(page.busy()).toBe(false));
    first.http.expectOne(basisUrl).flush(basisA);
    await resumed.whenStable();
    resumed.detectChanges();
    expect(page.recoveryReferences()).toHaveLength(1);
    expect(sessionStorage.getItem(firstLegacyKey)).toBeNull();
    expect(sessionStorage.getItem(secondLegacyKey)).not.toBeNull();
    expect(host.querySelector('[data-eqr-submit]')).toBeNull();

    const secondChoice = host.querySelector<HTMLSelectElement>('[data-eqr-recovery-choice]')!;
    secondChoice.value = secondLegacyKey;
    secondChoice.dispatchEvent(new Event('change', { bubbles: true }));
    resumed.detectChanges();
    host.querySelector<HTMLButtonElement>('[data-eqr-select-recovery]')!.click();
    resumed.detectChanges();
    expect(page.pending()?.requestKey).toBe(secondRequestKey);
    const secondRecoveryNotes = host.querySelector<HTMLTextAreaElement>('#eqr-recovery-notes')!;
    secondRecoveryNotes.value = notes;
    secondRecoveryNotes.dispatchEvent(new Event('input', { bubbles: true }));
    resumed.detectChanges();
    host.querySelector<HTMLButtonElement>('[data-eqr-retry]')!.click();
    const secondReplay = await waitForRequest(first.http, decisionUrl);
    expect(secondReplay.request.body).toEqual({ ...originalBody, requestKey: secondRequestKey });
    secondReplay.flush({ value: decisionId });
    await vi.waitFor(() => expect(page.busy()).toBe(false));
    first.http.expectOne(basisUrl).flush(basisA);
    await resumed.whenStable();
    resumed.detectChanges();
    expect(page.recoveryReferences()).toHaveLength(0);
    expect(sessionStorage.getItem(secondLegacyKey)).toBeNull();
    expect(host.querySelector<HTMLButtonElement>('[data-eqr-submit]')?.disabled).toBe(true);
    resumed.destroy();
  });

  it('requires renewed assent after editing each decision field', async () => {
    const { fixture, page, http } = setup();
    await loadBasis(http, fixture);
    const host = fixture.nativeElement as HTMLElement;
    enterNewDecision(fixture, 'CONCURRED', 'Original notes', true);
    fixture.detectChanges();
    for (const edit of [
      () => { const field = host.querySelector<HTMLSelectElement>('#eqr-decision-status')!; field.value = 'IN_PROGRESS'; field.dispatchEvent(new Event('change')); },
      () => { const field = host.querySelector<HTMLTextAreaElement>('#eqr-decision-notes')!; field.value = 'Changed notes'; field.dispatchEvent(new Event('input')); },
      () => { const field = host.querySelector<HTMLInputElement>('input[name="eqr-findings-discussed"]')!; field.checked = false; field.dispatchEvent(new Event('change')); },
    ]) {
      reviewExactBasis(fixture);
      fixture.detectChanges();
      expect(page.canSubmit(page.basis.data()!)).toBe(true);
      edit();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(page.reviewedExactBasis()).toBe(false);
      expect(page.canSubmit(page.basis.data()!)).toBe(false);
      expect(host.querySelector<HTMLInputElement>('input[name="eqr-exact-basis-reviewed"]')!.checked).toBe(false);
    }
    http.expectNone(decisionUrl);
  });

  it('freezes controls during hashing and dispatch and sends the reviewed snapshot', async () => {
    const { fixture, page, http } = setup();
    await loadBasis(http, fixture);
    enterNewDecision(fixture, 'CHANGES_REQUESTED', 'Original notes', true);
    fixture.detectChanges();
    reviewExactBasis(fixture);
    fixture.detectChanges();
    const digest = crypto.subtle.digest.bind(crypto.subtle);
    let release!: () => void;
    const gate = new Promise<void>(resolve => { release = resolve; });
    vi.spyOn(crypto.subtle, 'digest').mockImplementation(async (algorithm, data) => { await gate; return digest(algorithm, data); });
    const recording = page.recordDecision();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    for (const selector of ['#eqr-decision-status', '#eqr-decision-notes', 'input[name="eqr-findings-discussed"]', 'input[name="eqr-exact-basis-reviewed"]'])
      expect(host.querySelector<HTMLInputElement>(selector)!.disabled).toBe(true);
    http.expectNone(decisionUrl);
    release();
    const post = await waitForRequest(http, decisionUrl);
    fixture.detectChanges();
    expect(host.querySelector<HTMLTextAreaElement>('#eqr-recovery-notes')!.disabled).toBe(true);
    expect(post.request.body).toMatchObject({ status: 'CHANGES_REQUESTED', notes: 'Original notes', findingsDiscussed: true });
    post.flush({ value: decisionId });
    await recording;
    http.expectOne(basisUrl).flush(basisA);
  });

  it('keeps the actor claim during delayed hashing across session replacement', async () => {
    const { fixture, page, http, session } = setup();
    await loadBasis(http, fixture);
    enterNewDecision(fixture, 'CHANGES_REQUESTED', 'First notes', false);
    fixture.detectChanges(); reviewExactBasis(fixture); fixture.detectChanges();
    const digest = crypto.subtle.digest.bind(crypto.subtle);
    let release!: () => void;
    const gate = new Promise<void>(resolve => { release = resolve; });
    vi.spyOn(crypto.subtle, 'digest').mockImplementation(async (algorithm, data) => { await gate; return digest(algorithm, data); });
    const first = page.recordDecision();
    session.current.set({ ...firstSession, generation: '8' });
    fixture.detectChanges();
    http.expectOne(basisUrl).flush(basisA);
    fixture.detectChanges();
    enterNewDecision(fixture, 'CHANGES_REQUESTED', 'Replacement notes', false);
    fixture.detectChanges(); reviewExactBasis(fixture); fixture.detectChanges();
    await page.recordDecision();
    expect(page.error()).toContain('still in flight');
    release();
    await first;
    http.expectNone(decisionUrl);
    expect(sessionStorage.getItem(storageKey)).toBeNull();
    fixture.destroy();
  });

  it('ignores a delayed decision response after session replacement and retains the original reference', async () => {
    const { fixture, page, http, session } = setup();
    await loadBasis(http, fixture);
    enterNewDecision(fixture, 'CHANGES_REQUESTED', 'Original notes', false);
    fixture.detectChanges();
    reviewExactBasis(fixture);
    fixture.detectChanges();
    const emitted = vi.fn();
    page.decisionRecorded.subscribe(emitted);
    const recording = page.recordDecision();
    const post = await waitForRequest(http, decisionUrl);
    const raw = sessionStorage.getItem(storageKey);
    session.current.set({ ...firstSession, generation: '8' });
    fixture.detectChanges();
    http.expectOne(basisUrl).flush(basisB);
    post.flush({ value: decisionId });
    await recording;
    expect(emitted).not.toHaveBeenCalled();
    expect(page.message()).toBe('');
    expect(sessionStorage.getItem(storageKey)).toBe(raw);
    expect(page.pending()?.requestKey).toBe(post.request.body.requestKey);
    fixture.destroy();
  });

  it('clears only a definitive first-send refusal after navigating away', async () => {
    const { fixture, page, http } = setup();
    await loadBasis(http, fixture);
    enterNewDecision(fixture, 'CHANGES_REQUESTED', 'Original notes', false);
    fixture.detectChanges(); reviewExactBasis(fixture); fixture.detectChanges();
    const recording = page.recordDecision();
    const post = await waitForRequest(http, decisionUrl);
    expect(sessionStorage.getItem(storageKey)).not.toBeNull();
    fixture.destroy();
    post.flush({ code: 'generation.stale', message: 'The reviewed basis changed.' }, { status: 409, statusText: 'Conflict' });
    await recording;
    expect(sessionStorage.getItem(storageKey)).toBeNull();
  });

  it('clears a definitive refusal from the replacement session context', async () => {
    const { fixture, page, http, session } = setup();
    await loadBasis(http, fixture);
    enterNewDecision(fixture, 'CHANGES_REQUESTED', 'Original notes', false);
    fixture.detectChanges(); reviewExactBasis(fixture); fixture.detectChanges();
    const recording = page.recordDecision();
    const post = await waitForRequest(http, decisionUrl);
    const requestKey = post.request.body.requestKey as string;
    session.current.set({ ...firstSession, generation: '8' });
    fixture.detectChanges();
    http.expectOne(basisUrl).flush(basisB);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.pending()?.requestKey).toBe(requestKey);

    post.flush({ code: 'scope.denied', message: 'The role changed.' }, { status: 403, statusText: 'Forbidden' });
    await recording;
    await fixture.whenStable();
    fixture.detectChanges();

    expect(sessionStorage.getItem(storageKey)).toBeNull();
    expect(page.pending()).toBeNull();
    expect(page.recoveryReferences()).toHaveLength(0);
    expect(page.error()).toContain('earlier EQR request was refused');
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-eqr-submit]')).not.toBeNull();
    fixture.destroy();
  });

  it('recovers multiple saved requests one at a time and blocks new decisions', async () => {
    const first = setup();
    await loadBasis(first.http, first.fixture);
    const notes = 'The inventory valuation evidence requires a documented correction.';
    enterNewDecision(first.fixture, 'CHANGES_REQUESTED', notes, false);
    reviewExactBasis(first.fixture);
    await first.fixture.whenStable();
    first.fixture.detectChanges();
    const firstAttempt = first.page.recordDecision();
    const firstPost = await waitForRequest(first.http, decisionUrl, 'initial EQR decision');
    const firstBody = firstPost.request.body;
    const firstRaw = sessionStorage.getItem(storageKey)!;
    firstPost.flush({ code: 'temporarily.unavailable', message: 'try later' }, { status: 503, statusText: 'Unavailable' });
    await firstAttempt;

    const secondPending = JSON.parse(firstRaw) as Record<string, unknown>;
    secondPending['requestKey'] = '99999999-9999-4999-8999-999999999999';
    const secondRaw = JSON.stringify(secondPending);
    sessionStorage.setItem(legacyKey, secondRaw);
    first.fixture.destroy();
    first.session.current.set({ ...firstSession, generation: '8' });

    const resumed = TestBed.createComponent(EqrDecision);
    resumed.componentRef.setInput('engagementId', engagementId);
    resumed.componentRef.setInput('caseStatus', 'RECORDED_CONCURRENCE_NOT_CURRENT');
    resumed.detectChanges();
    await loadBasis(first.http, resumed);
    const page = resumed.componentInstance;
    const host = resumed.nativeElement as HTMLElement;
    expect(page.pendingUnavailable()).toBe(false);
    expect(page.pending()).toBeNull();
    expect(page.recoveryReferences()).toHaveLength(2);
    expect(host.querySelector('[data-eqr-submit]')).toBeNull();

    const recoveryChoice = host.querySelector<HTMLSelectElement>('[data-eqr-recovery-choice]')!;
    recoveryChoice.value = legacyKey;
    recoveryChoice.dispatchEvent(new Event('change', { bubbles: true }));
    resumed.detectChanges();
    host.querySelector<HTMLButtonElement>('[data-eqr-select-recovery]')!.click();
    resumed.detectChanges();
    expect(page.pending()?.requestKey).toBe(secondPending['requestKey']);
    const recoveryNotes = host.querySelector<HTMLTextAreaElement>('#eqr-recovery-notes')!;
    recoveryNotes.value = notes;
    recoveryNotes.dispatchEvent(new Event('input', { bubbles: true }));
    resumed.detectChanges();
    expect(host.querySelector<HTMLButtonElement>('[data-eqr-retry]')!.disabled).toBe(false);
    host.querySelector<HTMLButtonElement>('[data-eqr-retry]')!.click();
    const secondReplay = await waitForRequest(first.http, decisionUrl, 'selected legacy EQR replay');
    expect(secondReplay.request.body.requestKey).toBe(secondPending['requestKey']);
    expect(secondReplay.request.body.notes).toBe(notes);
    secondReplay.flush({ value: decisionId });
    await vi.waitFor(() => expect(page.busy()).toBe(false));
    first.http.expectOne(basisUrl).flush(basisB);
    await resumed.whenStable();
    resumed.detectChanges();
    expect(sessionStorage.getItem(legacyKey)).toBeNull();
    expect(sessionStorage.getItem(storageKey)).toBe(firstRaw);
    expect(page.pending()).toBeNull();
    expect(page.recoveryReferences()).toHaveLength(1);
    expect(host.querySelector('[data-eqr-submit]')).toBeNull();

    const remainingChoice = host.querySelector<HTMLSelectElement>('[data-eqr-recovery-choice]')!;
    remainingChoice.value = storageKey;
    remainingChoice.dispatchEvent(new Event('change', { bubbles: true }));
    resumed.detectChanges();
    host.querySelector<HTMLButtonElement>('[data-eqr-select-recovery]')!.click();
    resumed.detectChanges();
    expect(page.pending()?.requestKey).toBe(firstBody.requestKey);
    const remainingNotes = host.querySelector<HTMLTextAreaElement>('#eqr-recovery-notes')!;
    remainingNotes.value = notes;
    remainingNotes.dispatchEvent(new Event('input', { bubbles: true }));
    resumed.detectChanges();
    host.querySelector<HTMLButtonElement>('[data-eqr-retry]')!.click();
    const firstReplay = await waitForRequest(first.http, decisionUrl, 'remaining stable EQR replay');
    expect(firstReplay.request.body).toEqual(firstBody);
    firstReplay.flush({ value: decisionId });
    await vi.waitFor(() => expect(page.busy()).toBe(false));
    first.http.expectOne(basisUrl).flush(basisB);
    await resumed.whenStable();
    expect(sessionStorage.getItem(storageKey)).toBeNull();
    expect(sessionStorage.length).toBe(0);
    expect(host.querySelector('[data-eqr-submit]')).not.toBeNull();
    resumed.destroy();
  });

  it('keeps recovery unavailable when storage enumeration fails without replacing references', async () => {
    const raw = JSON.stringify({ version: 1, firmId: firstSession.firmId, actorUserId: firstSession.userId, sessionGeneration: '7',
      engagementId, requestKey: decisionId, ...basisA, status: 'IN_PROGRESS', findingsDiscussed: false, notesSalt: 'a'.repeat(32), notesHash: 'b'.repeat(64) });
    sessionStorage.setItem(legacyKey, raw);
    const { fixture, page, http, session } = setup();
    await loadBasis(http, fixture);
    expect(page.pending()?.requestKey).toBe(decisionId);
    vi.spyOn(Storage.prototype, 'key').mockImplementation(() => { throw new Error('unavailable'); });
    session.current.set({ ...firstSession, generation: '8' });
    fixture.detectChanges();
    await loadBasis(http, fixture);
    expect(page.pendingUnavailable()).toBe(true);
    expect(page.pending()).toBeNull();
    expect(sessionStorage.getItem(legacyKey)).toBe(raw);
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-eqr-submit]')).toBeNull();
    http.expectNone(decisionUrl);
    fixture.destroy();
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
    page.status = 'CONCURRED';
    page.notes = 'Draft for the first report';
    page.findingsDiscussed.set(true);
    fixture.detectChanges();
    page.reviewedExactBasis.set(true);
    page.acceptBasis(page.basis.data()!);
    expect(page.canSubmit(page.basis.data()!)).toBe(true);

    page.basis.reload();
    http.expectOne(basisUrl).flush({ ...basisB, reportDeliverableId: null, reportSha256: null });
    await fixture.whenStable();
    fixture.detectChanges();
    page.status = 'CONCURRED';
    page.findingsDiscussed.set(true);
    page.reviewedExactBasis.set(true);
    page.acceptBasis(page.basis.data()!);
    expect(page.canSubmit(page.basis.data()!)).toBe(false);
    page.status = 'IN_PROGRESS';
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
