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

  it('keeps reply and selected file through same-context polling, then clears and blocks submit on denial', async () => {
    vi.useFakeTimers();
    const fixture = TestBed.createComponent(ClientPbcRequest);
    fixture.detectChanges(); TestBed.tick();
    http.expectOne(`/api/ui/portal/requests/${id}`).flush(requestWorkspace());
    TestBed.tick(); fixture.detectChanges();

    const component = fixture.componentInstance;
    const root = fixture.nativeElement as HTMLElement;
    component.reply.controls.body.setValue('Unsent synthetic reply');
    const file = new File(['synthetic evidence'], 'support.pdf', { type: 'application/pdf' });
    component.file.set(file); component.hash.set('a'.repeat(64));
    fixture.detectChanges();

    await vi.advanceTimersByTimeAsync(30_000); fixture.detectChanges();
    const poll = http.expectOne(`/api/ui/portal/requests/${id}`);
    expect(component.reply.controls.body.value).toBe('Unsent synthetic reply');
    expect(component.file()).toBe(file);
    expect(component.hash()).toBe('a'.repeat(64));
    http.expectNone((request) => request.method === 'POST');
    poll.flush(requestWorkspace()); TestBed.tick(); fixture.detectChanges();
    expect(component.reply.controls.body.value).toBe('Unsent synthetic reply');
    expect(component.file()).toBe(file);

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
});
