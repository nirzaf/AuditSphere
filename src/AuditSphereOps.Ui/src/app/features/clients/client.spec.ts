import { describe, expect, it } from 'vitest';
import { decodeClient } from './client';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';

describe('Client Detail Contracts', () => {
  it('decodes a valid client payload', () => {
    const raw = {
      id: id1,
      name: 'Acme Global Holdings Ltd',
      status: 'ACTIVE',
      engagements: [
        {
          id: id2,
          serviceRoute: 'AUDIT',
          status: 'IN_PROGRESS',
          periodStart: '2026-01-01',
          periodEnd: '2026-12-31',
          professionalWorkBlocked: false,
        },
      ],
      contacts: [
        {
          id: id3,
          name: 'Jane Doe',
          email: 'jane.doe@acme.test',
          role: 'CFO',
          primary: true,
        },
      ],
      canManageContacts: true,
      safetyGeneration: '1001',
      canCreateEngagement: true,
    };

    const decoded = decodeClient(raw);
    expect(decoded.id).toBe(id1);
    expect(decoded.name).toBe('Acme Global Holdings Ltd');
    expect(decoded.engagements.length).toBe(1);
    expect(decoded.contacts.length).toBe(1);
    expect(decoded.contacts[0].primary).toBe(true);
    expect(decoded.canManageContacts).toBe(true);
  });

  it('rejects invalid client payloads', () => {
    expect(() => decodeClient(null)).toThrow();
    expect(() => decodeClient({})).toThrow();
    expect(() => decodeClient({ id: 'invalid-guid', name: 'Acme', status: 'ACTIVE', engagements: [] })).toThrow();
  });
});
