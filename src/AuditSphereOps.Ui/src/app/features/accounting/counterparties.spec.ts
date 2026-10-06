import { describe, expect, it } from 'vitest';
import { decodeCounterparties } from './counterparties';
const client = '11111111-1111-4111-8111-111111111111';
const id = '22222222-2222-4222-8222-222222222222';
const party = { id, clientId: client, legalName: 'Example', displayName: 'Example', role: 'BOTH', address: '', country: 'QA', taxIdentifier: '', contactDetails: '', paymentTerms: '', defaultCurrency: '', externalSystem: '', externalReference: '', createdByUserId: id, createdAt: '2026-10-06T00:00:00Z', revision: '1', effectiveAmendmentId: null };
const list = { clientId: client, role: null, page: 0, pageSize: 25, total: 1, bookkeepingActive: true, counterparties: [party] };
describe('Client counterparty transport', () => {
  it('accepts optional tax and a combined customer and supplier role', () => {
    expect(decodeCounterparties(list, client, null, 0).counterparties[0].taxIdentifier).toBe('');
    for (const role of ['CUSTOMER', 'SUPPLIER', 'BOTH']) expect(decodeCounterparties({ ...list, role }, client, role, 0).total).toBe(1);
  });
  it('rejects wrong client, filter, page, duplicates, identifiers and malformed state', () => {
    for (const change of [{ clientId: id }, { role: 'SUPPLIER' }, { page: 1 }, { total: -1 }, { bookkeepingActive: 'true' }, { total: 2, counterparties: [party, party] }, { counterparties: [{ ...party, id: 'bad' }] }, { counterparties: [{ ...party, clientId: id }] }]) expect(() => decodeCounterparties({ ...list, ...change }, client, null, 0)).toThrow();
    expect(() => decodeCounterparties({ ...list, role: 'SUPPLIER', counterparties: [{ ...party, role: 'CUSTOMER' }] }, client, 'SUPPLIER', 0)).toThrow();
  });
});
