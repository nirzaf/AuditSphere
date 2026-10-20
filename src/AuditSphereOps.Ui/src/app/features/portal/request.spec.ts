import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/session';
import { ClientPbcRequest } from './request';

const id = '40000000-0000-4000-8000-000000000001';
const requestWorkspace = (canWrite = true, canDelegate = true) => ({
  id, area: 'Synthetic request', objective: 'Supply synthetic support', instructions: 'Synthetic instructions',
  requestedFormat: 'PDF', dueDate: '2027-01-31', state: 'SENT', revision: '3',
  firstSignIn: { completed: true, identityPath: 'EXTERNAL_IDENTITY', canComplete: false, message: '' },
  canWrite, canManageDelegations: canDelegate, canDelegate, delegations: [], candidates: [], uploads: [], conversation: [], clarificationReason: null,
  hasMoreUploads: false, hasMoreConversation: false,
});

describe('Client request draft polling', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ClientPbcRequest],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id })) } }],
    });
    TestBed.inject(SessionService).current.set({ userId: id, firmId: '40000000-0000-4000-8000-000000000002', generation: '1', staff: false });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try { http.verify(); } finally { TestBed.resetTestingModule(); vi.useRealTimers(); }
  });

  it('associates reply validation with its error and blocks whitespace-only submission', () => {
    const fixture = TestBed.createComponent(ClientPbcRequest);
    fixture.detectChanges(); TestBed.tick();
    http.expectOne(`/api/ui/portal/requests/${id}`).flush(requestWorkspace());
    TestBed.tick(); fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('#reviewer-acceptance-heading')).toBeNull();
    expect(root.querySelector('#acceptance-receipt-heading')).toBeNull();
    const input = root.querySelector<HTMLTextAreaElement>('#portal-reply')!;
    const send = Array.from(root.querySelectorAll<HTMLButtonElement>('button'))
      .find(button => button.textContent?.trim() === 'Send reply')!;
    expect(input.getAttribute('aria-invalid')).toBeNull();
    fixture.componentInstance.reply.controls.body.markAsTouched(); fixture.detectChanges();
    expect(input.getAttribute('aria-invalid')).toBe('true');
    const error = root.querySelector<HTMLElement>('#' + input.getAttribute('aria-describedby'))!;
    expect(error.textContent).toContain('Enter a reply');
    expect(error.getAttribute('role')).toBe('alert');
    fixture.componentInstance.reply.controls.body.setValue('Corrected synthetic reply'); fixture.detectChanges();
    expect(fixture.componentInstance.reply.controls.body.valid).toBe(true);
    expect(send.disabled).toBe(false);
    expect(input.getAttribute('aria-invalid')).toBeNull();
    expect(input.getAttribute('aria-describedby')).toBeNull();
    expect(root.querySelector('#portal-reply-error')).toBeNull();
    fixture.componentInstance.reply.controls.body.setValue(' \t\n  '); fixture.detectChanges();
    expect(fixture.componentInstance.reply.controls.body.invalid).toBe(true);
    expect(send.disabled).toBe(true);
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.getAttribute('aria-describedby')).toBe('portal-reply-error');
    fixture.componentInstance.sendReply();
    http.expectNone(request => request.method === 'POST');
    fixture.destroy();
  });

  it('shows retained reviewer acceptance with its exact file, revisions, and UTC receipt time', () => {
    const fixture = TestBed.createComponent(ClientPbcRequest);
    fixture.detectChanges(); TestBed.tick();
    const acceptance = {
      id: '40000000-0000-4000-8000-000000000031',
      uploadIntentId: '40000000-0000-4000-8000-000000000032',
      acceptedAt: '2026-10-21T09:34:56Z',
      acceptedRequestRevision: '9223372036854775807',
      sourceRequestRevision: '9007199254740993',
      uploadIntentRevision: '12',
      fileName: 'October statements with supporting schedules.pdf',
      byteCount: '9007199254740993',
      sha256: 'c'.repeat(64),
    };
    http.expectOne(`/api/ui/portal/requests/${id}`).flush({ ...requestWorkspace(false, false), state: 'ACCEPTED', acceptance });
    TestBed.tick(); fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const heading = root.querySelector<HTMLElement>('#reviewer-acceptance-heading')!;
    const section = heading.closest('section')!;
    expect(section.getAttribute('aria-labelledby')).toBe('reviewer-acceptance-heading');
    expect(heading.textContent?.trim()).toBe('Recorded reviewer acceptance');
    const time = section.querySelector('time')!;
    expect(time.getAttribute('datetime')).toBe(acceptance.acceptedAt);
    expect(time.textContent?.trim()).toBe('2026-10-21 09:34 UTC');
    const rows = Array.from(section.querySelectorAll<HTMLDivElement>('.acceptance-details > div'))
      .map(row => [row.querySelector('dt')?.textContent?.trim(), row.querySelector('dd')?.textContent?.trim()]);
    expect(rows).toEqual([
      ['Acceptance receipt ID', acceptance.id],
      ['Upload intent ID', acceptance.uploadIntentId],
      ['File name', acceptance.fileName],
      ['Byte count', `${acceptance.byteCount} bytes`],
      ['SHA-256', acceptance.sha256],
      ['Accepted request revision', acceptance.acceptedRequestRevision],
      ['Source request revision', acceptance.sourceRequestRevision],
      ['Upload intent revision', acceptance.uploadIntentRevision],
    ]);
    expect(section.textContent).toContain('Acceptance recorded by assigned reviewer');
    expect(section.textContent).toContain('applies only to the exact uploaded file represented by this receipt');
    expect(section.textContent).toContain('does not indicate external delivery or legal certification');
    http.expectNone(request => request.method === 'POST');
    fixture.destroy();
  });

  it('reports no retained receipt for a terminal request without inventing an acceptance time', () => {
    const fixture = TestBed.createComponent(ClientPbcRequest);
    fixture.detectChanges(); TestBed.tick();
    http.expectOne(`/api/ui/portal/requests/${id}`).flush({ ...requestWorkspace(false, false), state: 'CLOSED' });
    TestBed.tick(); fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('#reviewer-acceptance-heading')).toBeNull();
    const heading = root.querySelector<HTMLElement>('#acceptance-receipt-heading')!;
    const section = heading.closest('section')!;
    expect(heading.textContent?.trim()).toBe('Acceptance receipt');
    expect(section.textContent).toContain('No retained acceptance receipt is available for this request.');
    expect(section.textContent).toContain('No acceptance time is shown.');
    expect(section.querySelector('time')).toBeNull();
    http.expectNone(request => request.method === 'POST');
    fixture.destroy();
  });

  it('lets a delegation manager revoke existing grants on a closed request without showing creation controls', async () => {
    const fixture = TestBed.createComponent(ClientPbcRequest);
    fixture.detectChanges(); TestBed.tick();
    const delegationId = '40000000-0000-4000-8000-000000000021';
    http.expectOne(`/api/ui/portal/requests/${id}`).flush({
      ...requestWorkspace(false, false), state: 'CLOSED', canManageDelegations: true,
      delegations: [{ id: delegationId, delegateUserId: '40000000-0000-4000-8000-000000000022',
        delegateName: 'Assigned colleague', createdAt: '2026-10-20T12:00:00Z' }],
    });
    TestBed.tick(); fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const section = root.querySelector<HTMLElement>('#delegate-heading')?.parentElement!;
    const revoke = section.querySelector<HTMLButtonElement>('button[aria-label="Revoke delegation for Assigned colleague"]')!;
    expect(revoke).not.toBeNull();
    expect(root.querySelector('#portal-delegate')).toBeNull();
    expect(section.textContent).toContain('New delegations cannot be added to an accepted or closed request.');
    expect(section.textContent).not.toContain('No other eligible client users are available.');

    revoke.click();
    const revokeRequest = http.expectOne(`/api/ui/portal/requests/${id}/delegations/${delegationId}/revoke`);
    expect(revokeRequest.request.method).toBe('POST');
    revokeRequest.flush({ value: true });
    await vi.waitFor(() => {
      TestBed.tick(); fixture.detectChanges();
      expect(root.textContent).toContain('Delegation revoked.');
    });
    http.expectOne(`/api/ui/portal/requests/${id}`).flush({
      ...requestWorkspace(false, false), state: 'CLOSED', canManageDelegations: true,
    });
    TestBed.tick(); fixture.detectChanges();
    fixture.destroy();
  });

  it('keeps reply and selected file through same-context polling, then clears and blocks submit on denial', async () => {
    vi.useFakeTimers();
    const fixture = TestBed.createComponent(ClientPbcRequest);
    fixture.detectChanges(); TestBed.tick();
    http.expectOne(`/api/ui/portal/requests/${id}`).flush(requestWorkspace());
    TestBed.tick(); fixture.detectChanges();

    const component = fixture.componentInstance;
    const root = fixture.nativeElement as HTMLElement;
    component.reply.controls.body.setValue('Unsent synthetic reply');
    component.delegation.controls.userId.setValue('40000000-0000-4000-8000-000000000009');
    const file = new File(['synthetic evidence'], 'support.pdf', { type: 'application/pdf' });
    component.file.set(file); component.hash.set('a'.repeat(64));
    fixture.detectChanges();

    await vi.advanceTimersByTimeAsync(30_000); fixture.detectChanges();
    const poll = http.expectOne(`/api/ui/portal/requests/${id}`);
    expect(component.reply.controls.body.value).toBe('Unsent synthetic reply');
    expect(component.file()).toBe(file);
    expect(component.hash()).toBe('a'.repeat(64));
    expect(component.delegation.controls.userId.value).toBe('40000000-0000-4000-8000-000000000009');
    http.expectNone((request) => request.method === 'POST');
    poll.flush(requestWorkspace()); TestBed.tick(); fixture.detectChanges();
    expect(component.reply.controls.body.value).toBe('Unsent synthetic reply');
    expect(component.file()).toBe(file);

    const session = TestBed.inject(SessionService);
    session.current.update(actor => actor ? { ...actor, generation: '2' } : null);
    TestBed.tick(); fixture.detectChanges();
    expect(component.reply.controls.body.value).toBe('');
    expect(component.delegation.controls.userId.value).toBe('');
    expect(component.file()).toBeNull();
    expect(component.hash()).toBe('');
    component.sendReply();
    http.expectNone(request => request.method === 'POST');
    http.expectOne(`/api/ui/portal/requests/${id}`).flush(requestWorkspace());
    TestBed.tick(); fixture.detectChanges();
    component.reply.controls.body.setValue('Draft after fresh authorized context');
    component.file.set(file); component.hash.set('a'.repeat(64));

    await vi.advanceTimersByTimeAsync(30_000); fixture.detectChanges();
    http.expectOne(`/api/ui/portal/requests/${id}`).flush({ code: 'scope.denied', message: 'Denied' },
      { status: 403, statusText: 'Forbidden' });
    TestBed.tick(); fixture.detectChanges();
    expect(component.reply.controls.body.value).toBe('');
    expect(component.delegation.controls.userId.value).toBe('');
    expect(component.file()).toBeNull();
    expect(component.hash()).toBe('');

    component.reply.controls.body.setValue('Attempt after authorization loss');
    component.sendReply();
    http.expectNone((request) => request.method === 'POST');
    expect(root.textContent).not.toContain('Unsent synthetic reply');
    fixture.destroy();
  });

  it.each([403, 409])('loads bounded history pages and clears protected content on history status %i', status => {
    const fixture = TestBed.createComponent(ClientPbcRequest);
    fixture.detectChanges(); TestBed.tick();
    const summary = { ...requestWorkspace(), hasMoreUploads: true, hasMoreConversation: true };
    http.expectOne(`/api/ui/portal/requests/${id}`).flush(summary);
    TestBed.tick(); fixture.detectChanges();

    const component = fixture.componentInstance;
    component.nextConversationPage(true); TestBed.tick(); fixture.detectChanges();
    const nextMessages = http.expectOne(`/api/ui/portal/requests/${id}/history?uploadPage=0&conversationPage=1&pageSize=25`);
    nextMessages.flush({ requestId: id, uploadPage: 0, conversationPage: 1, pageSize: 25,
      uploads: [], hasMoreUploads: true,
      conversation: [{ id: '40000000-0000-4000-8000-000000000011', at: '2026-10-17T13:00:00+03:00', speaker: 'Audit team', body: 'Next-page synthetic message' }],
      hasMoreConversation: false });
    TestBed.tick(); fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const conversationPages = root.querySelector<HTMLElement>('nav[aria-label="Conversation pages"]')!;
    expect(Array.from(conversationPages.querySelectorAll('button')).map(button => button.textContent?.trim()))
      .toEqual(['Previous messages', 'Next messages']);
    expect(root.textContent).toContain('Next-page synthetic message');
    expect(fixture.nativeElement.textContent).toContain('Synthetic request');
    const conversationTime = root.querySelector<HTMLElement>('.conversation time')!;
    expect(conversationTime.getAttribute('datetime')).toBe('2026-10-17T13:00:00+03:00');
    expect(conversationTime.textContent).toBe('2026-10-17 10:00 UTC');

    component.nextUploadPage(true); TestBed.tick(); fixture.detectChanges();
    const olderUploads = http.expectOne(`/api/ui/portal/requests/${id}/history?uploadPage=1&conversationPage=1&pageSize=25`);
    olderUploads.flush({ requestId: id, uploadPage: 1, conversationPage: 1, pageSize: 25,
      uploads: [{ id: '40000000-0000-4000-8000-000000000012', fileName: 'older.pdf', receivedByteCount: '1', declaredByteCount: '1',
        state: 'STAGED', transferState: null, sha256: 'a'.repeat(64), createdAt: '2026-10-16T13:00:00+03:00' }],
      hasMoreUploads: false,
      conversation: [{ id: '40000000-0000-4000-8000-000000000011', at: '2026-10-17T13:00:00+03:00', speaker: 'Audit team', body: 'Next-page synthetic message' }],
      hasMoreConversation: false });
    TestBed.tick(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('older.pdf');
    expect(fixture.nativeElement.textContent).toContain('Next-page synthetic message');
    const receipt = root.querySelector<HTMLElement>('.receipt')!;
    const createdTime = receipt.querySelector('time')!;
    expect(createdTime.getAttribute('datetime')).toBe('2026-10-16T13:00:00+03:00');
    expect(createdTime.textContent).toBe('2026-10-16 10:00 UTC');

    component.reply.controls.body.setValue('Private draft cleared after denial');
    component.previousUploadPage(); TestBed.tick(); fixture.detectChanges();
    http.expectOne(`/api/ui/portal/requests/${id}/history?uploadPage=0&conversationPage=1&pageSize=25`)
      .flush({ code: status === 403 ? 'scope.denied' : 'generation.stale', message: 'Refresh required' },
        { status, statusText: status === 403 ? 'Forbidden' : 'Conflict' });
    TestBed.tick(); fixture.detectChanges();
    expect(component.reply.controls.body.value).toBe('');
    expect(fixture.nativeElement.textContent).not.toContain('Synthetic request');
    expect(fixture.nativeElement.textContent).not.toContain('Next-page synthetic message');
    expect(fixture.nativeElement.textContent).not.toContain('older.pdf');
    fixture.destroy();
  });
});
