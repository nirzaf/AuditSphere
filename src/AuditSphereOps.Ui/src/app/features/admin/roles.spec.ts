import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { decode } from '../../core/decode';
import { SessionService } from '../../core/session';
import { accessUser, accessWorkspace } from './access-contracts';
import { RoleAssignmentDialog, roleChoices, roleScopeDescription } from './roles';

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
