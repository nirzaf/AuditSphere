import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { vi } from 'vitest';
import { TenantProvisioningDialog } from './provisioning';
import { SessionService } from '../../core/session';

const tenant = '11111111-1111-4111-8111-111111111111';
const client = '22222222-2222-4222-8222-222222222222';
const user = '33333333-3333-4333-8333-333333333333';
const operation = '44444444-4444-4444-8444-444444444444';
const workspace = { users: [], disabledOrRevoked: [], operations: [], groups: [], history: [],
  clients: [{ id: client, name: 'Approved client', parentId: null }], engagements: [], clientGroups: [] };
const result = { operationId: operation, state: 'BOUND', message: 'Local binding completed.', boundUserId: user,
  roleGrantId: operation, temporaryPassword: 'synthetic-memory-only-password', correlationId: 'synthetic-test' };

describe('Optional tenant provisioning security states', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [TenantProvisioningDialog], providers: [provideHttpClient(), provideHttpClientTesting(),
      { provide: MAT_DIALOG_DATA, useValue: { invite: false, tenantId: tenant, workspace } },
      { provide: MatDialogRef, useValue: { disableClose: false, close: vi.fn() } }] });
    TestBed.inject(SessionService).current.set({ userId: user, firmId: client, generation: '0', staff: true });
  });
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); TestBed.resetTestingModule(); });
  async function ready() {
    const fixture = TestBed.createComponent(TenantProvisioningDialog), http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/ui/administration/role-catalogue').flush(['Staff', 'ClientUser']);
    await fixture.whenStable();
    const c = fixture.componentInstance;
    c.form.setValue({ displayName: 'Synthetic joiner', upn: 'joiner@example.test', nickname: 'joiner', enabled: true,
      role: 'Staff', scope: 'CLIENT', clientId: client, engagementId: '', reason: 'Approved client team joiner' });
    c.step.set(4);
    return { fixture, http, c };
  }
  it('requires both reviews, keeps the password out of result state and clears it on close', async () => {
    const { fixture, http, c } = await ready();
    await c.submit(); http.expectNone('/api/ui/administration/directory/create');
    c.reviewed.setValue(true); await c.submit(); http.expectNone('/api/ui/administration/directory/create');
    c.passwordReviewed.setValue(true); const pending = c.submit();
    const request = http.expectOne('/api/ui/administration/directory/create');
    expect(request.request.body.request.scopeKind).toBe('CLIENT');
    expect(request.request.body.request.clientId).toBe(client);
    expect(request.request.body.request).not.toHaveProperty('password');
    request.flush({ value: result }); await pending;
    expect(c.password()).toBe(result.temporaryPassword);
    expect(c.result()?.temporaryPassword).toBeNull();
    expect(c.revealed()).toBe(false);
    c.close(); expect(c.password()).toBeNull();
    expect(TestBed.inject(MatDialogRef).close).toHaveBeenCalledWith(true);
    fixture.destroy(); expect(c.result()).toBeNull();
  });
  it('clears the secret and closes immediately when the trusted session changes', async () => {
    const { fixture, http, c } = await ready();
    c.reviewed.setValue(true); c.passwordReviewed.setValue(true);
    const pending = c.submit(); http.expectOne('/api/ui/administration/directory/create').flush({ value: result }); await pending;
    TestBed.inject(SessionService).clear(); fixture.detectChanges();
    expect(c.password()).toBeNull();
    expect(TestBed.inject(MatDialogRef).close).toHaveBeenCalledWith(false);
    fixture.destroy();
  });
  it('fences resubmission after a lost response and never retries a create automatically', async () => {
    const { fixture, http, c } = await ready();
    c.reviewed.setValue(true); c.passwordReviewed.setValue(true);
    const pending = c.submit(); http.expectOne('/api/ui/administration/directory/create').flush({}, { status: 503, statusText: 'Unavailable' }); await pending;
    expect(c.uncertain()).toBe(true); expect(c.step()).toBe(6); expect(c.password()).toBeNull();
    await c.submit(); http.expectNone('/api/ui/administration/directory/create'); fixture.destroy();
  });
  it('clears the password at the sixty-second limit', async () => {
    const { fixture, http, c } = await ready();
    vi.useFakeTimers();
    try {
      c.reviewed.setValue(true); c.passwordReviewed.setValue(true);
      const pending = c.submit(); http.expectOne('/api/ui/administration/directory/create').flush({ value: result }); await pending;
      vi.advanceTimersByTime(60000); expect(c.password()).toBeNull();
    } finally { vi.useRealTimers(); fixture.destroy(); }
  });
});
