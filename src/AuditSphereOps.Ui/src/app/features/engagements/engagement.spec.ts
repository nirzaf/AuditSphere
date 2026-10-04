import { describe, expect, it } from 'vitest';
import { decodeEngagement, engagementWorkflowLink } from './engagement';
import { decodeClient } from '../clients/client';
const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
describe('Workspace contracts', () => {
  it('keeps multi-segment engagement workflow routes as router segments', () => {
    expect(engagementWorkflowLink(id, 'reconciliation/new')).toEqual([
      '/app/engagements',
      id,
      'reconciliation',
      'new',
    ]);
    expect(engagementWorkflowLink(id, 'specialists/new')).toEqual([
      '/app/engagements',
      id,
      'specialists',
      'new',
    ]);
  });

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
      serviceProfileId: 'Standard',
      createdAt: '2026-01-02T12:30:00Z',
      canViewClientProfile: false,
      holdMetrics: { total: 0, active: 0, released: 0 },
      paging: { holdPage: 0, holdPageSize: 10 },
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
        commercialName: null,
        registrationNumber: null,
        jurisdiction: null,
        createdAt: '2026-01-01T00:00:00Z',
        metrics: { engagements: 0, workBlocked: 0, contacts: 0 },
        paging: { engagementPage: 0, engagementPageSize: 10, contactPage: 0, contactPageSize: 10 },
        portalIntent: null,
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
