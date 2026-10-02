import { describe, expect, it } from 'vitest';
import { decodeClients, decodeWorkspace, decodeSources } from './workspace';

const id = '11111111-1111-4111-8111-111111111111';
describe('Accounting contracts', () => {
  it('binds package selection to the exact reviewed period and currency', () => {
    const prior = { id, revision: '2', currency: 'QAR', code: '2026', start: '2026-01-01', end: '2026-12-31', basis: 'STATUTORY', status: 'CLOSED' };
    const response = { periodId: id, revision: '2', hasMore: false, items: [{ id, hash: 'f'.repeat(64), currency: 'QAR' }] };
    expect(decodeSources(response, prior).items[0].hash).toBe('f'.repeat(64));
    expect(() => decodeSources({ ...response, revision: '3' }, prior)).toThrow();
    expect(() => decodeSources({ ...response, items: [{ id, hash: 'f'.repeat(64), currency: 'USD' }] }, prior)).toThrow();
  });
  it('accepts bounded scoped clients and missing setup without fabricated defaults', () => {
    expect(decodeClients({ items: [{ id, name: 'Client', profileConfigured: false }], total: 1, page: 0, pageSize: 25 }).items.length).toBe(1);
    expect(decodeWorkspace({ clientId: id, name: 'Client', profile: null, periods: [], hasMorePeriods: false, books: [], hasMoreBooks: false, amendments: [], hasMoreAmendments: false, openingBridges: [], canReviewOpening: false }).profile).toBeNull();
  });
  it('rejects invalid identity, page size and revisions encoded as numbers', () => {
    expect(() => decodeClients({ items: [], total: 0, page: 0, pageSize: 0 })).toThrow();
    expect(() => decodeClients({ items: [{ id: 'bad', name: 'Client', profileConfigured: true }], total: 1, page: 0, pageSize: 25 })).toThrow();
    expect(() => decodeWorkspace({ clientId: id, name: 'Client', profile: null, hasMorePeriods: false, books: [], hasMoreBooks: false, amendments: [], hasMoreAmendments: false, openingBridges: [], canReviewOpening: false,
      periods: [{ id, revision: 1, code: '2026', start: '2026-01-01', end: '2026-12-31', basis: 'IFRS', currency: 'QAR', status: 'DRAFT' }] })).toThrow();
  });
});
