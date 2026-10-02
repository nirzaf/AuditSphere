import { describe, expect, it } from 'vitest';
import { decodeEngagement } from './engagement';
import { decodeClient } from '../clients/client';
const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
describe('Workspace contracts', () => {
  it('preserves exact revision and validates hold state', () => {
    const value = {
      id,
      clientId: id,
      clientName: 'Client',
      serviceRoute: 'Audit',
      status: 'Draft',
      periodStart: '2026-01-01',
      periodEnd: '2026-12-31',
      generation: '9007199254740993',
      professionalWorkBlocked: true,
      canActivate: false,
      holds: [],
    };
    expect(decodeEngagement(value).generation).toBe('9007199254740993');
    expect(() => decodeEngagement({ ...value, generation: 9007199254740993 })).toThrow();
    expect(() => decodeEngagement({ ...value, holds: [{ released: 'false' }] })).toThrow();
  });
  it('rejects malformed client identities and unbounded engagement arrays', () => {
    expect(
      decodeClient({
        id,
        name: 'Client',
        status: 'Active',
        contacts: [],
        canManageContacts: false,
        canCreateEngagement: false,
        safetyGeneration: '1',
        engagements: [],
      }).id,
    ).toBe(id);
    expect(() =>
      decodeClient({
        id: 'guessed',
        name: 'Client',
        status: 'Active',
        contacts: [],
        canManageContacts: false,
        canCreateEngagement: false,
        safetyGeneration: '1',
        engagements: [],
      }),
    ).toThrow();
    expect(() =>
      decodeClient({
        id,
        name: 'Client',
        status: 'Active',
        contacts: [],
        canManageContacts: false,
        canCreateEngagement: false,
        safetyGeneration: '1',
        engagements: Array(101).fill({}),
      }),
    ).toThrow();
  });
});
