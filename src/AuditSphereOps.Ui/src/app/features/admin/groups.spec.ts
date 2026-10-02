import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { vi } from 'vitest';
import { ManagedGroupsDialog } from './groups';
import { SessionService } from '../../core/session';

const group = '11111111-1111-4111-8111-111111111111';
const user = '22222222-2222-4222-8222-222222222222';
const preview = { managedGroupId: group, groupName: 'Managed team', groupObjectId: group,
  userId: user, userName: 'Local member', userObjectId: user, tenantId: group, existingMembership: false, observedAt: '2026-10-02T00:00:00Z' };

describe('Managed group change review', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [ManagedGroupsDialog], providers: [provideHttpClient(), provideHttpClientTesting(),
      { provide: MAT_DIALOG_DATA, useValue: { users: [], disabledOrRevoked: [], groups: [], operations: [], clients: [], engagements: [], history: [], clientGroups: [] } },
      { provide: MatDialogRef, useValue: { disableClose: false, close: vi.fn() } }] });
    TestBed.inject(SessionService).current.set({ userId: user, firmId: group, generation: '0', staff: true });
  });
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); TestBed.resetTestingModule(); });
  it('sends the reviewed membership state and clears the review after a stale-state refusal', async () => {
    const fixture = TestBed.createComponent(ManagedGroupsDialog), c = fixture.componentInstance, http = TestBed.inject(HttpTestingController);
    c.change.setValue({ groupId: group, userId: user, action: 'ADD', reason: 'Approved collaboration access' });
    const reading = c.preview(); http.expectOne('/api/ui/administration/groups/' + group + '/preview').flush({ value: preview }); await reading;
    await c.save(); http.expectNone('/api/ui/administration/groups/change');
    c.reviewed.setValue(true); const saving = c.save();
    const request = http.expectOne('/api/ui/administration/groups/change');
    expect(request.request.body.request.expectedExistingMembership).toBe(false);
    expect(request.request.body.request.managedGroupId).toBe(group);
    request.flush({ code: 'revision.stale', message: 'Membership changed.' }, { status: 409, statusText: 'Conflict' }); await saving;
    expect(c.review()).toBeNull(); expect(c.uncertain()).toBe(false);
    await c.save(); http.expectNone('/api/ui/administration/groups/change'); fixture.destroy();
  });
  it('discards a late preview after changing the reviewed user or reason', async () => {
    const fixture = TestBed.createComponent(ManagedGroupsDialog), c = fixture.componentInstance, http = TestBed.inject(HttpTestingController);
    c.change.setValue({ groupId: group, userId: user, action: 'ADD', reason: 'Original membership review' });
    const reading = c.preview(); c.change.controls.reason.setValue('Changed membership review');
    http.expectOne('/api/ui/administration/groups/' + group + '/preview').flush({ value: preview }); await reading;
    expect(c.review()).toBeNull(); fixture.destroy();
  });
});
