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
  canWrite, canDelegate, delegations: [], candidates: [], uploads: [], conversation: [], clarificationReason: null,
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

  it('loads independently bounded history pages for the same request and clears the request on history denial', () => {
    const fixture = TestBed.createComponent(ClientPbcRequest);
    fixture.detectChanges(); TestBed.tick();
    const summary = { ...requestWorkspace(), hasMoreUploads: true, hasMoreConversation: true };
    http.expectOne(`/api/ui/portal/requests/${id}`).flush(summary);
    TestBed.tick(); fixture.detectChanges();

    const component = fixture.componentInstance;
    component.nextConversationPage(true); TestBed.tick(); fixture.detectChanges();
    const olderMessages = http.expectOne(`/api/ui/portal/requests/${id}/history?uploadPage=0&conversationPage=1&pageSize=25`);
    olderMessages.flush({ requestId: id, uploadPage: 0, conversationPage: 1, pageSize: 25,
      uploads: [], hasMoreUploads: true,
      conversation: [{ id: '40000000-0000-4000-8000-000000000011', at: '2026-10-17T10:00:00Z', speaker: 'Audit team', body: 'Older synthetic message' }],
      hasMoreConversation: false });
    TestBed.tick(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Older synthetic message');
    expect(fixture.nativeElement.textContent).toContain('Synthetic request');

    component.nextUploadPage(true); TestBed.tick(); fixture.detectChanges();
    const olderUploads = http.expectOne(`/api/ui/portal/requests/${id}/history?uploadPage=1&conversationPage=1&pageSize=25`);
    olderUploads.flush({ requestId: id, uploadPage: 1, conversationPage: 1, pageSize: 25,
      uploads: [{ id: '40000000-0000-4000-8000-000000000012', fileName: 'older.pdf', receivedByteCount: '1', declaredByteCount: '1',
        state: 'STAGED', transferState: null, sha256: 'a'.repeat(64), createdAt: '2026-10-16T10:00:00Z' }],
      hasMoreUploads: false,
      conversation: [{ id: '40000000-0000-4000-8000-000000000011', at: '2026-10-17T10:00:00Z', speaker: 'Audit team', body: 'Older synthetic message' }],
      hasMoreConversation: false });
    TestBed.tick(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('older.pdf');
    expect(fixture.nativeElement.textContent).toContain('Older synthetic message');

    component.reply.controls.body.setValue('Private draft cleared after denial');
    component.previousUploadPage(); TestBed.tick(); fixture.detectChanges();
    http.expectOne(`/api/ui/portal/requests/${id}/history?uploadPage=0&conversationPage=1&pageSize=25`)
      .flush({ code: 'scope.denied', message: 'Denied' }, { status: 403, statusText: 'Forbidden' });
    TestBed.tick(); fixture.detectChanges();
    expect(component.reply.controls.body.value).toBe('');
    expect(fixture.nativeElement.textContent).not.toContain('Synthetic request');
    expect(fixture.nativeElement.textContent).not.toContain('Older synthetic message');
    expect(fixture.nativeElement.textContent).not.toContain('older.pdf');
    fixture.destroy();
  });
});
