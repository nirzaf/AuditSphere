import { capabilityAvailable, groupMembers, groupMembershipPreview, recoverableOperation } from './operations-contracts';
import { decode } from '../../core/decode';

describe('Tenant operation boundary contracts', () => {
  const capability = { capability: 'TENANT_USER_PROVISIONING', displayName: 'Create users', permission: 'User.Create', enabled: true,
    state: 'VERIFIED', lastVerifiedAt: '2026-10-02T00:00:00Z', stale: false, diagnosticCode: 'verified' };
  it('offers provisioning only when explicitly enabled with a fresh verified capability', () => {
    expect(capabilityAvailable([capability], capability.capability)).toBe(true);
    for (const changed of [{ enabled: false }, { state: 'NOT_GRANTED' }, { stale: true }, { state: 'BLOCKED_EXTERNAL' }])
      expect(capabilityAvailable([{ ...capability, ...changed }], capability.capability)).toBe(false);
    expect(capabilityAvailable([capability], 'GUEST_INVITATION')).toBe(false);
  });
  it('requires reconciliation for uncertain outcomes and local binding recovery only for accepted identities', () => {
    expect(recoverableOperation('UNKNOWN', 'CREATE_TENANT_USER')).toBe(true);
    expect(recoverableOperation('DISPATCHING', 'ADD_GROUP_MEMBER')).toBe(true);
    expect(recoverableOperation('ACCEPTED', 'INVITE_GUEST')).toBe(true);
    for (const state of ['REQUESTED', 'AUTHORIZED', 'BOUND', 'FAILED', 'CONFLICT'])
      expect(recoverableOperation(state, 'CREATE_TENANT_USER')).toBe(false);
    expect(recoverableOperation('ACCEPTED', 'ADD_GROUP_MEMBER')).toBe(false);
  });
  it('refuses oversized member pages and email-only membership identities', () => {
    expect(() => decode(groupMembers, { members: Array.from({ length: 51 }, () => ({})), nextPageToken: null })).toThrow();
    expect(() => decode(groupMembershipPreview, { managedGroupId: 'email@example.test', userId: 'email@example.test' })).toThrow();
  });
});
