import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { Api } from '../../core/api';
import { of } from 'rxjs';
import { UserAccess } from './access';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { decode } from '../../core/decode';
import { SessionService } from '../../core/session';
import { accessUser, accessWorkspace } from './access-contracts';
import { AccessRevocationDialog, RoleAssignmentDialog, roleChoices, roleScopeDescription } from './roles';

describe('Role assignment identity choices', () => {
  it('offers only client roles with explicit client or engagement scope for a guest client', () => {
    const choices = roleChoices('Client (Guest)', ['Administrator', 'Staff', 'ClientUser']);
    expect(choices.roles).toEqual(['ClientUser']);
    expect(choices.scopes).toEqual(['CLIENT', 'ENGAGEMENT']);
    expect(choices.initialRole).toBe('ClientUser');
  });
  it('keeps staff roles separate from client identities', () => {
    expect(roleChoices('Staff (Member)', ['Administrator', 'Staff', 'ClientUser']).roles).toEqual(['Administrator', 'Staff']);
  });
});


const client = '11111111-1111-4111-8111-111111111111';
const engagementA = '22222222-2222-4222-8222-222222222222';
const engagementB = '33333333-3333-4333-8333-333333333333';
const grantA = '44444444-4444-4444-8444-444444444444';
const grantB = '55555555-5555-4555-8555-555555555555';
const userId = '66666666-6666-4666-8666-666666666666';
const group = '77777777-7777-4777-8777-777777777777';
const at = '2026-10-16T10:00:00Z';
const line = (grantId: string, engagementId: string) => ({ grantId, role: 'Staff', scopeKind: 'ENGAGEMENT' as const,
  clientId: client, engagementId, groupId: null, grantedAt: at, expiresAt: null, groupGrant: false });
function dialogData() {
  const user = decode(accessUser, { userId, displayName: 'Synthetic staff', microsoftIdentity: 'staff@example.test',
    tenantId: client, objectId: userId, accountType: 'Staff (Member)', directoryStatus: 'ENABLED', auditSphereStatus: 'ENABLED',
    access: [line(grantA, engagementA), line(grantB, engagementB)], invitationId: null, invitationStatus: 'NONE',
    lastMicrosoftVerification: at, lastAuditSphereAccess: null, createdBy: 'Synthetic administrator', createdDate: at });
  const workspace = decode(accessWorkspace, { users: [user], disabledOrRevoked: [], operations: [], groups: [], history: [], grantHistory: [],
    clients: [{ id: client, name: 'Shared client', parentId: null }],
    engagements: [{ id: engagementA, name: 'Same engagement label', parentId: client }, { id: engagementB, name: 'Same engagement label', parentId: client }],
    clientGroups: [{ id: group, name: 'Synthetic group', parentId: null }] });
  return { user, workspace };
}
function reviewedScope(engagementId = engagementB) {
  return { digest: 'a'.repeat(64), review: { userId, userKind: 'Staff', currentAccess: [line(grantA, engagementA), line(grantB, engagementB)],
    proposed: line('00000000-0000-0000-0000-000000000000', engagementId), addedCapabilities: [], removedCapabilities: [],
    scopeExpansion: false, scopeReduction: false, independenceImpact: [], warnings: [], blockingReasons: [] } };
}

describe('Exact role scope description', () => {
  it('distinguishes sibling engagements even with identical display names and client IDs', () => {
    const { workspace } = dialogData();
    const first = roleScopeDescription(line(grantA, engagementA), workspace), second = roleScopeDescription(line(grantB, engagementB), workspace);
    expect(first).toContain('Same engagement label'); expect(first).toContain(engagementA); expect(first).toContain(client);
    expect(second).toContain('Same engagement label'); expect(second).toContain(engagementB); expect(second).toContain(client);
    expect(first).not.toBe(second); expect(first).not.toContain(engagementB); expect(second).not.toContain(engagementA);
  });
  it('keeps exact scope IDs when a name is unavailable and never substitutes client scope for a missing engagement', () => {
    const { workspace } = dialogData();
    expect(roleScopeDescription(line(grantA, engagementA), { ...workspace, engagements: [] })).toContain(engagementA);
    expect(roleScopeDescription({ ...line(grantA, engagementA), engagementId: null }, workspace)).toBe(`Engagement identifier unavailable · Client Shared client (${client})`);
  });
  it('describes client, group and firm scopes using their own identifiers', () => {
    const { workspace } = dialogData();
    expect(roleScopeDescription({ scopeKind: 'CLIENT', clientId: client, engagementId: null, groupId: null }, workspace)).toBe(`Client Shared client (${client})`);
    expect(roleScopeDescription({ scopeKind: 'GROUP', clientId: null, engagementId: null, groupId: group }, workspace)).toBe(`Client group Synthetic group (${group})`);
    expect(roleScopeDescription({ scopeKind: 'FIRM_WIDE', clientId: null, engagementId: null, groupId: null }, workspace)).toBe('Firm-wide');
  });
});

describe('Role assignment exact engagement review rendering', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [RoleAssignmentDialog], providers: [provideHttpClient(), provideHttpClientTesting(),
      { provide: MAT_DIALOG_DATA, useValue: dialogData() }, { provide: MatDialogRef, useValue: { close: vi.fn() } }] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).current.set({ userId, firmId: client, generation: '1', staff: true });
  });
  afterEach(() => { http.verify({ ignoreCancelled: true }); TestBed.resetTestingModule(); });
  function open() {
    const f = TestBed.createComponent(RoleAssignmentDialog); f.detectChanges();
    http.expectOne('/api/ui/administration/role-catalogue').flush(['Staff', 'ClientUser']); f.detectChanges(); TestBed.tick();
    f.componentInstance.form.patchValue({ role: 'Staff', scopeKind: 'ENGAGEMENT', clientId: client, engagementId: engagementB,
      replacesGrantId: grantA, reason: 'Move to the exact sibling engagement.' }); f.detectChanges(); return f;
  }
  it('renders exact sibling IDs in engagement and replacement options, current/proposed access and the reviewed replacement', async () => {
    const f = open(), c = f.componentInstance;
    const first = f.nativeElement.querySelector(`#role-replaces option[value="${grantA}"]`) as HTMLOptionElement;
    const second = f.nativeElement.querySelector(`#role-replaces option[value="${grantB}"]`) as HTMLOptionElement;
    expect(first.textContent).toContain(engagementA); expect(first.textContent).not.toContain(engagementB);
    expect(second.textContent).toContain(engagementB); expect(second.textContent).not.toContain(engagementA);
    expect((f.nativeElement.querySelector(`#role-engagement option[value="${engagementB}"]`) as HTMLOptionElement).textContent).toContain(engagementB);
    const pending = c.preview(), request = http.expectOne('/api/ui/administration/access/preview');
    expect(request.request.body.engagementId).toBe(engagementB); expect(request.request.body.replacesGrantId).toBe(grantA);
    request.flush({ value: reviewedScope() }); await pending; f.detectChanges();
    const current = f.nativeElement.querySelector('[aria-label="Reviewed current access"]') as HTMLElement;
    const proposed = f.nativeElement.querySelector('[aria-label="Reviewed proposed access"]') as HTMLElement;
    const replacement = f.nativeElement.querySelector('[aria-label="Reviewed replacement grant"]') as HTMLElement;
    expect(current.textContent).toContain(engagementA); expect(current.textContent).toContain(engagementB);
    expect(proposed.textContent).toContain(engagementB); expect(proposed.textContent).not.toContain(engagementA);
    expect(replacement.textContent).toContain(engagementA); expect(replacement.textContent).toContain(grantA); expect(replacement.textContent).not.toContain(engagementB);
    expect(c.confirmed.value).toBe(false); http.expectNone('/api/ui/administration/access/assign');
  });
  it('clears the reviewed scope and assent when the selected engagement changes', async () => {
    const f = open(), c = f.componentInstance, pending = c.preview();
    http.expectOne('/api/ui/administration/access/preview').flush({ value: reviewedScope() }); await pending;
    c.confirmed.setValue(true); c.form.controls.engagementId.setValue(engagementA); f.detectChanges();
    expect(c.review()).toBeNull(); expect(c.confirmed.value).toBe(false); expect(c.replacementGrant()).toBeUndefined();
    expect(f.nativeElement.querySelector('[aria-label="Reviewed proposed access"]')).toBeNull();
    await c.save(); http.expectNone('/api/ui/administration/access/assign');
  });
});


describe('Access revocation reviewed reason and dialog lifetime', () => {
  const close = vi.fn(), command = vi.fn(), epoch = signal(0);
  beforeEach(() => {
    close.mockReset(); command.mockReset(); epoch.set(0);
    TestBed.configureTestingModule({ imports: [AccessRevocationDialog], providers: [
      { provide: Api, useValue: { command } },
      { provide: SessionService, useValue: { current: signal({ staff: true }), invalidation: epoch } },
      { provide: MatDialogRef, useValue: { close } },
      { provide: MAT_DIALOG_DATA, useValue: { displayName: 'Synthetic user', microsoftIdentity: 'synthetic@example.test',
        grant: { grantId: '00000000-0000-0000-0000-000000000001', role: 'Staff', scopeKind: 'FIRM_WIDE', groupGrant: false } } },
    ] });
  });
  afterEach(() => TestBed.resetTestingModule());
  function open() {
    const fixture = TestBed.createComponent(AccessRevocationDialog); fixture.detectChanges();
    fixture.componentInstance.form.controls.reason.setValue('Reviewed original reason');
    fixture.componentInstance.form.controls.reviewed.setValue(true); return fixture;
  }
  it('requires fresh assent when the reviewed reason changes', async () => {
    const fixture = open(), c = fixture.componentInstance;
    c.form.controls.reason.setValue('A different reviewed reason');
    expect(c.form.controls.reviewed.value).toBe(false); await c.save(); expect(command).not.toHaveBeenCalled();
  });
  it('refuses dispatch immediately after session invalidation before the closing effect runs', async () => {
    const fixture = open(), c = fixture.componentInstance; epoch.set(1); await c.save();
    expect(command).not.toHaveBeenCalled();
  });
  it('refuses dispatch from a destroyed dialog instance', async () => {
    const fixture = open(), c = fixture.componentInstance; fixture.destroy(); await c.save();
    expect(command).not.toHaveBeenCalled();
  });
  it('ignores a late acknowledgement after the dialog is destroyed', async () => {
    let finish!: (value: unknown) => void; command.mockReturnValue(new Promise(resolve => { finish = resolve; }));
    const fixture = open(), pending = fixture.componentInstance.save(); fixture.destroy();
    finish({ ok: true }); await pending; expect(close).not.toHaveBeenCalled();
  });
  it('closes protected grant context on a definitive authority refusal without retrying', async () => {
    command.mockResolvedValue({ ok: false, unknown: false, status: 403, code: 'scope.denied', message: 'Access denied.' });
    const fixture = open(); await fixture.componentInstance.save();
    expect(close).toHaveBeenCalledTimes(1); expect(close).toHaveBeenCalledWith('reauthorize'); expect(command).toHaveBeenCalledTimes(1);
  });
  it('retains a lost acknowledgement as uncertain and never resends automatically', async () => {
    command.mockResolvedValue({ ok: false, unknown: true, status: 0, code: '', message: 'Outcome unknown.' });
    const fixture = open(), c = fixture.componentInstance; await c.save(); await c.save();
    expect(c.uncertain()).toBe(true); expect(command).toHaveBeenCalledTimes(1); expect(close).not.toHaveBeenCalled();
  });
});


describe('Revocation authority refusal refreshes the parent roster', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('reloads protected access when the dialog requests reauthorization', () => {
    const reload = vi.fn(), open = vi.fn(() => ({ afterClosed: () => of('reauthorize') }));
    TestBed.configureTestingModule({ providers: [
      { provide: Api, useValue: { resource: () => ({ data: () => null, reload, loading: () => false, error: () => '' }) } },
      { provide: SessionService, useValue: { current: signal({ staff: true }), invalidation: signal(0) } },
      { provide: MatDialog, useValue: { open } },
    ] });
    const id = '00000000-0000-0000-0000-000000000001', at = '2026-10-16T00:00:00Z';
    const user = decode(accessUser, { userId: id, displayName: 'Synthetic user', microsoftIdentity: 'synthetic@example.test',
      tenantId: id, objectId: id, accountType: 'Staff (Member)', directoryStatus: 'Verified', auditSphereStatus: 'Active',
      access: [{ grantId: id, role: 'Staff', scopeKind: 'FIRM_WIDE', clientId: null, engagementId: null, groupId: null,
        grantedAt: at, expiresAt: null, groupGrant: false }], invitationId: null, invitationStatus: 'NONE',
      lastMicrosoftVerification: null, lastAuditSphereAccess: null, createdBy: 'Synthetic fixture', createdDate: at });
    const c = TestBed.runInInjectionContext(() => new UserAccess()); c.revoke(user, user.access[0]);
    expect(reload).toHaveBeenCalledTimes(1);
  });
});
