import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, type TestRequest } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SessionService } from '../../core/session';
import { ClientOffboardingReadiness } from './client-offboarding-readiness';
import { decodeOffboardingReadiness } from './client-contracts';

const clientId = '11111111-1111-4111-8111-111111111111';
const otherId = '22222222-2222-4222-8222-222222222222';
const readinessDigest = 'a'.repeat(64);
type OffboardingBody = {
  clientId: string;
  isArchived: boolean;
  archiveRevision: string;
  assessedAt: string;
  overallState: string;
  readinessDigest: string;
  sections: Array<{
    key: string;
    title: string;
    state: string;
    ownerRoute: string | null;
    items: Array<{ key: string; label: string; state: string; count: number | null; detail: string | null }>;
  }>;
  coverageNotes: string[];
};
const body = (id = clientId): OffboardingBody => ({
  clientId: id, isArchived: false, archiveRevision: '0', assessedAt: '2026-10-31T10:00:00Z', overallState: 'ACTION_REQUIRED',
  readinessDigest,
  sections: [
    { key: 'WORK', title: 'Open work', state: 'ACTION_REQUIRED', ownerRoute: '/app/work',
      items: [{ key: 'OPEN_TASKS', label: 'Open or in-progress tasks', state: 'ACTION_REQUIRED', count: 2, detail: null }] },
    { key: 'FINANCE', title: 'Money', state: 'UNKNOWN', ownerRoute: null,
      items: [{ key: 'OUTSTANDING_INVOICES', label: 'Posted invoices with an outstanding balance', state: 'UNKNOWN', count: null, detail: 'Your access does not include firm finance for this client.' }] },
  ],
  coverageNotes: ['NO_FINDINGS is not evidence that offboarding is complete.'],
});

const fullyAssessedBody = (id = clientId) => ({
  clientId: id, isArchived: false, archiveRevision: '0', assessedAt: '2026-10-31T10:00:00Z', overallState: 'ACTION_REQUIRED',
  readinessDigest,
  sections: [
    { key: 'WORK', title: 'Open work', state: 'ACTION_REQUIRED', ownerRoute: '/app/work',
      items: [{ key: 'OPEN_TASKS', label: 'Open or in-progress tasks', state: 'ACTION_REQUIRED', count: 2, detail: null }] },
    { key: 'FINANCE', title: 'Money', state: 'NO_FINDINGS', ownerRoute: null,
      items: [{ key: 'OUTSTANDING_INVOICES', label: 'Posted invoices with an outstanding balance', state: 'NO_FINDINGS', count: 0, detail: null }] },
  ],
  coverageNotes: ['NO_FINDINGS is not evidence that offboarding is complete.'],
});

const withFeeReconciliation = (count: number, detail: string) => {
  const value = body();
  value.sections.push({ key: 'FEE_MILESTONES', title: 'Fee agreement', state: 'ACTION_REQUIRED',
    ownerRoute: '/app/finance/fee-agreements',
    items: [{ key: 'FEE_RECONCILIATION', label: 'Advance invoice awaiting milestone reconciliation',
      state: 'ACTION_REQUIRED', count, detail }] });
  return value;
};
const committedRecovery = (requestKey: string, notes: string, currentArchiveRevision = '1') => ({
  outcome: 'COMMITTED_RECEIPT', clientId, actorUserId: clientId, requestKey, readinessDigest,
  expectedArchiveRevision: '0', currentArchiveRevision,
  receipt: {
    recordId: otherId, clientId, status: 'BLOCKED_DECISION', readinessOverallState: 'ACTION_REQUIRED',
    readinessDigest, dispositionNotes: notes, acknowledgedActionItems: [], decidedByUserId: clientId,
    decidedAt: '2026-11-08T10:00:00Z', isArchived: false, archiveRevision: '0', requestKey,
  },
});
const supersededRecovery = (requestKey: string, currentArchiveRevision = '1') => ({
  outcome: 'SUPERSEDED_WITHOUT_COMMIT', clientId, actorUserId: clientId, requestKey, readinessDigest,
  expectedArchiveRevision: '0', currentArchiveRevision, receipt: null,
});

describe('client offboarding readiness', () => {
  it('rejects an UNKNOWN item that carries a count, and a count that contradicts its state', () => {
    const unknownWithCount = body(); unknownWithCount.sections[1].items[0].count = 0 as never;
    expect(() => decodeOffboardingReadiness(unknownWithCount)).toThrow();
    const clearWithFindings = body(); clearWithFindings.sections[0].items[0].state = 'NO_FINDINGS';
    expect(() => decodeOffboardingReadiness(clearWithFindings)).toThrow();
    const hiddenAsClear = body(); hiddenAsClear.overallState = 'NO_FINDINGS';
    expect(() => decodeOffboardingReadiness(hiddenAsClear)).toThrow();
    expect(decodeOffboardingReadiness(body()).sections).toHaveLength(2);
  });

  it('accepts fee detail through 2,000 characters and reconciliation counts through 100 only', () => {
    for (const length of [1_999, 2_000]) {
      const decoded = decodeOffboardingReadiness(withFeeReconciliation(100, 'x'.repeat(length)));
      const item = decoded.sections.find(section => section.key === 'FEE_MILESTONES')!.items[0];
      expect(item.detail).toHaveLength(length);
      expect(item.count).toBe(100);
    }
    expect(() => decodeOffboardingReadiness(withFeeReconciliation(100, 'x'.repeat(2_001)))).toThrow();
    expect(() => decodeOffboardingReadiness(withFeeReconciliation(101, 'x'))).toThrow();
  });

  describe('component', () => {
    let http: HttpTestingController;
    beforeEach(() => {
      sessionStorage.clear();
      TestBed.configureTestingModule({ imports: [ClientOffboardingReadiness], providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
      http = TestBed.inject(HttpTestingController);
      TestBed.inject(SessionService).current.set({ userId: clientId, firmId: clientId, generation: '1', staff: true });
    });
    afterEach(() => { http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); sessionStorage.clear(); vi.restoreAllMocks(); });

    it('loads only on request, shows unreadable evidence as Unknown, and clears when the client changes', () => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId);
      fixture.detectChanges();
      http.expectNone(`/api/ui/clients/${clientId}/offboarding-readiness`);
      (fixture.nativeElement.querySelector('[data-offboarding-check]') as HTMLButtonElement).click();
      http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(body());
      fixture.detectChanges();
      const text = fixture.nativeElement.textContent as string;
      expect(text).toContain('Open or in-progress tasks: 2');
      expect(text).toContain('Posted invoices with an outstanding balance: Unknown');
      fixture.componentRef.setInput('clientId', otherId);
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).not.toContain('Open or in-progress tasks');
    });

    it('shows the bounded reconciliation preview and submits its reviewed digest', async () => {
      const feeBody = withFeeReconciliation(20,
        'Finding 1; Finding 2; Preview limited to 2,000 characters: showing 2 of 20 reconciliation findings. Open the fee-agreement workspace for full source evidence.');
      feeBody.readinessDigest = 'b'.repeat(64);
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId);
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('[data-offboarding-check]') as HTMLButtonElement).click();
      http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(feeBody);
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain('Advance invoice awaiting milestone reconciliation: 20');
      expect(fixture.nativeElement.textContent).toContain('Preview limited to 2,000 characters');
      const evidenceLink = fixture.nativeElement.querySelector('a[href="/app/finance/fee-agreements"]') as HTMLAnchorElement;
      expect(evidenceLink.textContent).toContain('Open fee-agreement workspace for full source evidence');

      const textarea = fixture.nativeElement.querySelector('#offboarding-disposition-notes') as HTMLTextAreaElement;
      textarea.value = 'Reviewed the bounded fee reconciliation preview and owner evidence.';
      textarea.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('[data-offboarding-closeout]') as HTMLButtonElement).click();
      await vi.waitFor(() => expect(fixture.componentInstance.pending()).not.toBeNull());
      const request = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout`);
      expect(request.request.body.expectedReadinessDigest).toBe(feeBody.readinessDigest);
      request.flush({
        recordId: otherId, clientId, status: 'BLOCKED_DECISION', readinessOverallState: 'ACTION_REQUIRED',
        readinessDigest: feeBody.readinessDigest, dispositionNotes: 'Reviewed the bounded fee reconciliation preview and owner evidence.',
        acknowledgedActionItems: [], decidedByUserId: clientId, isArchived: false, archiveRevision: '0',
        requestKey: request.request.body.requestKey,
      });
      fixture.detectChanges();
    });

    it('discards a response for a client that is no longer shown', () => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId);
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('[data-offboarding-check]') as HTMLButtonElement).click();
      const pending = http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`);
      fixture.componentRef.setInput('clientId', otherId);
      fixture.detectChanges();
      if (!pending.cancelled) pending.flush(body());
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).not.toContain('Open or in-progress tasks');
    });

    it('records an immutable BLOCKED_DECISION review even when a section remains UNKNOWN', async () => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId);
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('[data-offboarding-check]') as HTMLButtonElement).click();
      http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(body());
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('UNKNOWN and action-required items remain unresolved');
      const textarea = fixture.nativeElement.querySelector('#offboarding-disposition-notes') as HTMLTextAreaElement;
      textarea.value = 'Finance access was not available during this review.';
      textarea.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('[data-offboarding-closeout]') as HTMLButtonElement).click();

      await vi.waitFor(() => expect(fixture.componentInstance.pending()).not.toBeNull());
      const request = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout`);
      expect(request.request.body.expectedRevision).toBe('0');
      expect(request.request.body.expectedReadinessDigest).toBe(readinessDigest);
      expect(request.request.body.acknowledgedActionItems).toEqual([]);
      expect(request.request.body.archiveClientOnCloseout).toBe(false);
      expect(request.request.body.requestKey).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
      request.flush({
        recordId: otherId, clientId, status: 'BLOCKED_DECISION', readinessOverallState: 'ACTION_REQUIRED',
        readinessDigest, dispositionNotes: 'Finance access was not available during this review.',
        acknowledgedActionItems: [], decidedByUserId: clientId, isArchived: false, archiveRevision: '0',
        requestKey: request.request.body.requestKey,
      });
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain('A BLOCKED_DECISION review record was saved');
    });

    it('records offboarding closeout decision when fully assessed', async () => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId);
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('[data-offboarding-check]') as HTMLButtonElement).click();
      http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(fullyAssessedBody());
      fixture.detectChanges();

      const textarea = fixture.nativeElement.querySelector('#offboarding-disposition-notes') as HTMLTextAreaElement;
      expect(textarea).not.toBeNull();
      textarea.value = 'Reviewed all open tasks and outstanding invoices with Partner.';
      textarea.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      const closeoutBtn = fixture.nativeElement.querySelector('[data-offboarding-closeout]') as HTMLButtonElement;
      expect(closeoutBtn.disabled).toBe(false);
      closeoutBtn.click();

      await vi.waitFor(() => expect(fixture.componentInstance.pending()).not.toBeNull());
      const postReq = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout`);
      expect(postReq.request.method).toBe('POST');
      expect(postReq.request.body.dispositionNotes).toContain('Reviewed all open tasks');
      expect(postReq.request.body.acknowledgedActionItems).toEqual([]);
      expect(postReq.request.body.requestKey).toBeDefined();
      postReq.flush({
        recordId: otherId, clientId, status: 'BLOCKED_DECISION', readinessOverallState: 'ACTION_REQUIRED',
        readinessDigest, dispositionNotes: 'Reviewed all open tasks and outstanding invoices with Partner.',
        acknowledgedActionItems: [], decidedByUserId: clientId, isArchived: false, archiveRevision: '0',
        requestKey: postReq.request.body.requestKey,
      });
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('A BLOCKED_DECISION review record was saved');
    });

    it('reuses the same idempotency key after an unknown network outcome', async () => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId);
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('[data-offboarding-check]') as HTMLButtonElement).click();
      http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(fullyAssessedBody());
      fixture.detectChanges();
      const textarea = fixture.nativeElement.querySelector('#offboarding-disposition-notes') as HTMLTextAreaElement;
      textarea.value = 'Verified the listed items only.';
      textarea.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      (fixture.nativeElement.querySelector('[data-offboarding-closeout]') as HTMLButtonElement).click();
      await vi.waitFor(() => expect(fixture.componentInstance.pending()).not.toBeNull());
      const first = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout`);
      const firstKey = first.request.body.requestKey;
      first.error(new ProgressEvent('error'));
      fixture.detectChanges();

      (fixture.nativeElement.querySelector('[data-offboarding-closeout]') as HTMLButtonElement).click();
      let retry!: TestRequest;
      await vi.waitFor(() => { retry = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout`); });
      expect(retry.request.body.requestKey).toBe(firstKey);
      expect(retry.request.body.expectedReadinessDigest).toBe(readinessDigest);
      retry.flush({
        recordId: otherId, clientId, status: 'BLOCKED_DECISION', readinessOverallState: 'ACTION_REQUIRED',
        readinessDigest, dispositionNotes: 'Verified the listed items only.',
        acknowledgedActionItems: [], decidedByUserId: clientId, isArchived: false, archiveRevision: '0',
        requestKey: firstKey,
      });
    });

    it('fences edited intent and recovers the original request after navigation', async () => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId); fixture.detectChanges();
      fixture.componentInstance.load();
      http.expectOne('/api/ui/clients/' + clientId + '/offboarding-readiness').flush(body());
      const component = fixture.componentInstance;
      component.dispositionNotes = 'Original private review notes.';
      void component.recordCloseout(component.result()!);
      let first!: TestRequest;
      await vi.waitFor(() => { first = http.expectOne('/api/ui/clients/' + clientId + '/offboarding-closeout'); });
      const key = first.request.body.requestKey;
      first.error(new ProgressEvent('error'));
      expect(Array.from({ length: sessionStorage.length }, (_, i) => sessionStorage.getItem(sessionStorage.key(i)!)).join()).not.toContain('Original private review notes.');
      component.dispositionNotes = 'Changed review notes.';
      await component.recordCloseout(component.result()!);
      http.expectNone('/api/ui/clients/' + clientId + '/offboarding-closeout');
      TestBed.inject(SessionService).invalidation.update(value => value + 1);
      fixture.detectChanges();
      expect(component.pending()).toBeNull();
      expect(component.result()).toBeNull();
      component.load();
      http.expectOne('/api/ui/clients/' + clientId + '/offboarding-readiness').flush(body());
      expect(component.pending()?.requestKey).toBe(key);
      fixture.componentRef.setInput('clientId', otherId); fixture.detectChanges();
      expect(component.dispositionNotes).toBe('');
      expect(component.pending()).toBeNull();
      fixture.componentRef.setInput('clientId', clientId); fixture.detectChanges();
      component.load();
      http.expectOne('/api/ui/clients/' + clientId + '/offboarding-readiness').flush(body());
      expect(component.pending()?.requestKey).toBe(key);
      component.dispositionNotes = 'Original private review notes.';
      void component.recordCloseout(component.result()!);
      let retry!: TestRequest;
      await vi.waitFor(() => { retry = http.expectOne('/api/ui/clients/' + clientId + '/offboarding-closeout'); });
      expect(retry.request.body.requestKey).toBe(key);
      expect(retry.request.body.expectedReadinessDigest).toBe(readinessDigest);
      retry.flush({ recordId: otherId, clientId: otherId, status: 'BLOCKED_DECISION', readinessOverallState: 'ACTION_REQUIRED',
        readinessDigest, dispositionNotes: 'Original private review notes.', acknowledgedActionItems: [],
        decidedByUserId: clientId, isArchived: false, archiveRevision: '0', requestKey: key });
      expect(component.closeoutSuccess()).toBe(false);
      expect(component.pending()?.requestKey).toBe(key);
      void component.recordCloseout(component.result()!);
      await vi.waitFor(() => { retry = http.expectOne('/api/ui/clients/' + clientId + '/offboarding-closeout'); });
      retry.flush({ recordId: otherId, clientId, status: 'BLOCKED_DECISION', readinessOverallState: 'ACTION_REQUIRED',
        readinessDigest, dispositionNotes: 'Original private review notes.', acknowledgedActionItems: [],
        decidedByUserId: clientId, isArchived: false, archiveRevision: '0', requestKey: key });
      expect(component.closeoutSuccess()).toBe(true);
      expect(component.pending()).toBeNull();
    });

    it('does not overwrite a concurrent same-tab review reference', async () => {
      const first = TestBed.createComponent(ClientOffboardingReadiness);
      const second = TestBed.createComponent(ClientOffboardingReadiness);
      first.componentRef.setInput('clientId', clientId);
      second.componentRef.setInput('clientId', clientId);
      for (const fixture of [first, second]) {
        fixture.detectChanges();
        fixture.componentInstance.load();
        http.expectOne('/api/ui/clients/' + clientId + '/offboarding-readiness').flush(body());
      }
      first.componentInstance.dispositionNotes = 'First review';
      second.componentInstance.dispositionNotes = 'Second review';
      await Promise.all([first.componentInstance.recordCloseout(first.componentInstance.result()!),
        second.componentInstance.recordCloseout(second.componentInstance.result()!)]);
      const requests = http.match('/api/ui/clients/' + clientId + '/offboarding-closeout');
      expect(requests).toHaveLength(1);
      const sent = requests[0].request.body;
      const blocked = sent.dispositionNotes === 'First review' ? second.componentInstance : first.componentInstance;
      expect(blocked.error()).toMatch(/still in flight|recovery reference changed/);
      requests[0].flush({ recordId: otherId, clientId, status: 'BLOCKED_DECISION', readinessOverallState: 'ACTION_REQUIRED',
        readinessDigest, dispositionNotes: sent.dispositionNotes, acknowledgedActionItems: [], decidedByUserId: clientId,
        isArchived: false, archiveRevision: '0', requestKey: sent.requestKey });
    });

    it('does not send a retry from another component while the original request is in flight', async () => {
      const first = TestBed.createComponent(ClientOffboardingReadiness);
      const second = TestBed.createComponent(ClientOffboardingReadiness);
      first.componentRef.setInput('clientId', clientId); second.componentRef.setInput('clientId', clientId);
      first.detectChanges(); second.detectChanges();
      first.componentInstance.load();
      http.expectOne('/api/ui/clients/' + clientId + '/offboarding-readiness').flush(body());
      first.componentInstance.dispositionNotes = 'Original review';
      await first.componentInstance.recordCloseout(first.componentInstance.result()!);
      const original = http.expectOne('/api/ui/clients/' + clientId + '/offboarding-closeout');
      second.componentInstance.load();
      http.expectOne('/api/ui/clients/' + clientId + '/offboarding-readiness').flush(body());
      second.componentInstance.dispositionNotes = 'Original review';
      await second.componentInstance.recordCloseout(second.componentInstance.result()!);
      http.expectNone('/api/ui/clients/' + clientId + '/offboarding-closeout');
      expect(second.componentInstance.error()).toContain('still in flight');
      original.flush({ recordId: otherId, clientId, status: 'BLOCKED_DECISION', readinessOverallState: 'ACTION_REQUIRED',
        readinessDigest, dispositionNotes: 'Original review', acknowledgedActionItems: [], decidedByUserId: clientId,
        isArchived: false, archiveRevision: '0', requestKey: original.request.body.requestKey });
      expect(first.componentInstance.closeoutSuccess()).toBe(true);
    });

    it('does not clear a replacement reference when an older receipt arrives', async () => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId); fixture.detectChanges();
      const component = fixture.componentInstance;
      component.load(); http.expectOne('/api/ui/clients/' + clientId + '/offboarding-readiness').flush(body());
      component.dispositionNotes = 'Original review';
      await component.recordCloseout(component.result()!);
      const request = http.expectOne('/api/ui/clients/' + clientId + '/offboarding-closeout');
      const key = sessionStorage.key(0)!;
      const replacement = { ...component.pending()!, requestKey: otherId };
      sessionStorage.setItem(key, JSON.stringify(replacement));
      request.flush({ recordId: otherId, clientId, status: 'BLOCKED_DECISION', readinessOverallState: 'ACTION_REQUIRED',
        readinessDigest, dispositionNotes: 'Original review', acknowledgedActionItems: [], decidedByUserId: clientId,
        isArchived: false, archiveRevision: '0', requestKey: request.request.body.requestKey });
      expect(component.closeoutSuccess()).toBe(false);
      expect(JSON.parse(sessionStorage.getItem(key)!).requestKey).toBe(otherId);
    });
    it.each([
      { status: 429, error: {}, retained: true },
      { status: 409, error: {}, retained: true },
      { status: 409, error: { code: 'generation.stale' }, retained: false },
      { status: 403, error: { code: 'scope.denied' }, retained: false },
      { status: 400, error: { code: 'csrf.invalid' }, retained: false },
    ])('clears only an authoritative first-attempt rejection ($status, $retained)', async ({ status, error, retained }) => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId); fixture.detectChanges();
      const component = fixture.componentInstance;
      component.load(); http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(body());
      component.dispositionNotes = 'Original notes'; await component.recordCloseout(component.result()!);
      http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout`).flush(error, { status, statusText: 'Rejected' });
      expect(component.pending() !== null).toBe(retained);
      expect(sessionStorage.length).toBe(retained ? 1 : 0);
    });

    it('fences a session change during hashing before effects reset the displayed readiness', async () => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId); fixture.detectChanges();
      const component = fixture.componentInstance;
      component.load(); http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(body());
      component.dispositionNotes = 'Original notes';
      let release!: (value: ArrayBuffer) => void;
      vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(() => new Promise<ArrayBuffer>(resolve => { release = resolve; }));
      const command = component.recordCloseout(component.result()!);
      TestBed.inject(SessionService).invalidation.update(value => value + 1);
      release(new ArrayBuffer(32)); await command;
      http.expectNone(`/api/ui/clients/${clientId}/offboarding-closeout`);
      expect(sessionStorage.length).toBe(0);
      fixture.detectChanges();
      expect(component.result()).toBeNull(); expect(component.closeoutLoading()).toBe(false);
    });

    it.each([
      { status: 400, code: 'crm.invalid' },
      { status: 409, code: 'generation.stale' },
      { status: 403, code: 'scope.denied' },
      { status: 400, code: 'csrf.invalid' },
    ])('resolves a received first-attempt $status rejection after session invalidation without updating the new view', async ({ status, code }) => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId); fixture.detectChanges();
      const component = fixture.componentInstance;
      component.load(); http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(body());
      component.dispositionNotes = 'Original notes'; await component.recordCloseout(component.result()!);
      const request = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout`);
      TestBed.inject(SessionService).invalidation.update(value => value + 1);
      // The response arrives before the reset effect can cancel the subscription.
      request.flush({ code }, { status, statusText: 'Rejected' });
      expect(component.error()).toBe('');
      expect(component.closeoutSuccess()).toBe(false);
      fixture.detectChanges();
      component.load(); http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(body());
      expect(component.pending()).toBeNull();
      expect(sessionStorage.length).toBe(0);
    });

    it('retains an unknown original through session replacement and a later definitive retry rejection', async () => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId); fixture.detectChanges();
      const component = fixture.componentInstance;
      component.load(); http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(body());
      component.dispositionNotes = 'Original notes'; await component.recordCloseout(component.result()!);
      const request = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout`), originalKey = request.request.body.requestKey;
      TestBed.inject(SessionService).invalidation.update(value => value + 1);
      request.error(new ProgressEvent('error'));
      fixture.detectChanges();
      component.load(); http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(body());
      expect(component.pending()?.requestKey).toBe(originalKey);
      component.dispositionNotes = 'Original notes'; await component.recordCloseout(component.result()!);
      const retry = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout`);
      expect(retry.request.body.requestKey).toBe(originalKey);
      retry.flush({ code: 'generation.stale' }, { status: 409, statusText: 'Rejected' });
      expect(component.pending()?.requestKey).toBe(originalKey);
      expect(sessionStorage.length).toBe(1);
    });

    it('clears only a server-proven terminal non-commit and requires fresh readiness before a new review', async () => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId); fixture.detectChanges();
      const component = fixture.componentInstance;
      component.load(); http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(body());
      component.dispositionNotes = 'Original notes'; await component.recordCloseout(component.result()!);
      const first = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout`);
      const original = component.pending()!;
      first.error(new ProgressEvent('error'));

      await component.recordCloseout(component.result()!);
      const retry = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout`);
      expect(retry.request.body.requestKey).toBe(original.requestKey);
      retry.flush({ code: 'generation.stale' }, { status: 409, statusText: 'Stale' });
      expect(component.pending()?.requestKey).toBe(original.requestKey);
      expect(sessionStorage.length).toBe(1);

      await component.recoverCloseout();
      const recovery = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout/recovery`);
      expect(recovery.request.body).toEqual({ requestKey: original.requestKey, expectedRevision: '0',
        expectedReadinessDigest: readinessDigest, dispositionNotes: 'Original notes', acknowledgedActionItems: [] });
      recovery.flush(supersededRecovery(original.requestKey));
      expect(component.pending()).toBeNull();
      expect(component.closeoutSuccess()).toBe(false);
      expect(component.result()).toBeNull();
      expect(sessionStorage.length).toBe(0);
      expect(component.error()).toContain('Refresh readiness before starting a new review');

      component.load();
      http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush({ ...body(), isArchived: true,
        archiveRevision: '1', readinessDigest: 'b'.repeat(64) });
      expect(component.result()?.archiveRevision).toBe('1');
      expect(component.pending()).toBeNull();
    });

    it('recovers the exact committed receipt after archive state changes', async () => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId); fixture.detectChanges();
      const component = fixture.componentInstance;
      component.load(); http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(body());
      component.dispositionNotes = 'Original notes'; await component.recordCloseout(component.result()!);
      const first = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout`);
      const original = component.pending()!;
      first.error(new ProgressEvent('error'));

      await component.recoverCloseout();
      const recovery = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout/recovery`);
      recovery.flush(committedRecovery(original.requestKey, 'Original notes'));
      expect(component.closeoutSuccess()).toBe(true);
      expect(component.pending()).toBeNull();
      expect(sessionStorage.length).toBe(0);
    });

    it('retains pending recovery on generic stale errors and mismatched terminal identities', async () => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId); fixture.detectChanges();
      const component = fixture.componentInstance;
      component.load(); http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(body());
      component.dispositionNotes = 'Original notes'; await component.recordCloseout(component.result()!);
      const first = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout`);
      const original = component.pending()!;
      first.error(new ProgressEvent('error'));

      await component.recoverCloseout();
      http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout/recovery`).flush(
        { code: 'generation.stale' }, { status: 409, statusText: 'Not terminal' });
      expect(component.pending()?.requestKey).toBe(original.requestKey);
      expect(sessionStorage.length).toBe(1);

      await component.recoverCloseout();
      const mismatched = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout/recovery`);
      mismatched.flush(supersededRecovery(otherId));
      expect(component.pending()?.requestKey).toBe(original.requestKey);
      expect(sessionStorage.length).toBe(1);
      expect(component.error()).toContain('did not match the exact pending request');
    });

    it('retains the exact request when a terminal proof has a regressed archive revision', async () => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId); fixture.detectChanges();
      const component = fixture.componentInstance;
      component.load(); http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(body());
      component.dispositionNotes = 'Original notes'; await component.recordCloseout(component.result()!);
      const first = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout`);
      const original = component.pending()!;
      first.error(new ProgressEvent('error'));

      await component.recoverCloseout();
      http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout/recovery`).flush({
        ...supersededRecovery(original.requestKey), currentArchiveRevision: '0'
      });
      expect(component.pending()?.requestKey).toBe(original.requestKey);
      expect(sessionStorage.length).toBe(1);
      expect(component.error()).toContain('invalid recovery result');
    });

    it('does not dispatch recovery when the stored reference has been replaced', async () => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId); fixture.detectChanges();
      const component = fixture.componentInstance;
      component.load(); http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(body());
      component.dispositionNotes = 'Original notes'; await component.recordCloseout(component.result()!);
      const first = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout`);
      first.error(new ProgressEvent('error'));
      const key = sessionStorage.key(0)!;
      const replacement = JSON.stringify({ ...component.pending()!, requestKey: otherId });
      sessionStorage.setItem(key, replacement);

      await component.recoverCloseout();
      http.expectNone(`/api/ui/clients/${clientId}/offboarding-closeout/recovery`);
      expect(sessionStorage.getItem(key)).toBe(replacement);
      expect(component.pending()?.requestKey).not.toBe(otherId);
      expect(component.error()).toContain('recovery reference changed');
    });

    it('does not clear a changed recovery reference on an old definitive rejection', async () => {
      const fixture = TestBed.createComponent(ClientOffboardingReadiness);
      fixture.componentRef.setInput('clientId', clientId); fixture.detectChanges();
      const component = fixture.componentInstance;
      component.load(); http.expectOne(`/api/ui/clients/${clientId}/offboarding-readiness`).flush(body());
      component.dispositionNotes = 'Original notes'; await component.recordCloseout(component.result()!);
      const request = http.expectOne(`/api/ui/clients/${clientId}/offboarding-closeout`);
      const key = sessionStorage.key(0)!, replacement = JSON.stringify({ ...component.pending()!, requestKey: otherId });
      sessionStorage.setItem(key, replacement);
      TestBed.inject(SessionService).invalidation.update(value => value + 1);
      request.flush({ code: 'generation.stale' }, { status: 409, statusText: 'Rejected' });
      expect(sessionStorage.getItem(key)).toBe(replacement);
      expect(component.error()).toBe('');
    });
  });
});
