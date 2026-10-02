import { describe, expect, it } from 'vitest';
import { decode } from '../../core/decode';
import { accessLine, roleReview } from './access-contracts';
import { adminOverview } from './overview';
describe('administration contracts', () => {
  it('rejects unsupported local scope kinds and incomplete grants', () => {
    const line = { grantId: crypto.randomUUID(), role: 'Staff', scopeKind: 'CLIENT', clientId: crypto.randomUUID(), engagementId: null, groupId: null,
      grantedAt: '2026-10-02T10:00:00Z', expiresAt: null, groupGrant: false };
    expect(decode(accessLine, line).scopeKind).toBe('CLIENT');
    expect(() => decode(accessLine, { ...line, scopeKind: 'GLOBAL_ADMINISTRATOR' })).toThrow();
    expect(() => decode(roleReview, { digest: 'bad', review: {} })).toThrow();
  });
  it('does not turn a connection badge into a complete capability dashboard', () => {
    expect(() => decode(adminOverview, { state: 'VERIFIED', cards: [], progress: [] })).toThrow();
  });
});
