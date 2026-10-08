import { describe, expect, it } from 'vitest';
import { decodeCounterpartyHistory } from './counterparty-history';
const client = '11111111-1111-4111-8111-111111111111';
const party = '22222222-2222-4222-8222-222222222222';
const proposal = '33333333-3333-4333-8333-333333333333';
const reviewer = '44444444-4444-4444-8444-444444444444';
const current = { id: party, clientId: client, legalName: 'Example', displayName: 'Example', role: 'BOTH', address: 'Reviewed address', country: 'QA', taxIdentifier: '', contactDetails: '', paymentTerms: '', defaultCurrency: '', externalSystem: '', externalReference: '', createdByUserId: party, createdAt: '2026-10-06T00:00:00Z', revision: '2', effectiveAmendmentId: proposal };
const amendment = { id: proposal, counterpartyId: party, revision: '2', displayName: 'Example', address: 'Reviewed address', taxIdentifier: '', contactDetails: '', paymentTerms: '', reason: 'Updated address', proposedByUserId: party, createdAt: '2026-10-06T00:00:00Z', decision: 'APPROVE', reviewReason: 'Independent check', reviewedByUserId: reviewer };
const history = { current, bookkeepingActive: true, page: 0, pageSize: 25, total: 1, effectiveAmendment: amendment, amendments: [amendment] };
describe('Counterparty revision contract', () => {
  it('accepts a reviewed effective revision and separately supplied paged evidence', () => {
    expect(decodeCounterpartyHistory(history, client, party).current.address).toBe('Reviewed address');
    expect(decodeCounterpartyHistory({ ...history, page: 1, amendments: [] }, client, party, 1).effectiveAmendment?.id).toBe(proposal);
  });
  it('accepts matching evidence regardless of JSON property order', () => {
    const reordered = Object.fromEntries(Object.entries(amendment).reverse());
    expect(decodeCounterpartyHistory({ ...history, effectiveAmendment: reordered }, client, party).current.revision).toBe('2');
  });
  it('rejects wrong scope, page, duplicates, unapproved profiles and self-review', () => {
    for (const changes of [{ current: { ...current, clientId: reviewer } }, { page: 1 }, { effectiveAmendment: null }, { amendments: [amendment, amendment], total: 2 }, { effectiveAmendment: { ...amendment, address: 'Other' } }, { amendments: [], effectiveAmendment: { ...amendment, decision: null, reviewReason: null, reviewedByUserId: null } }, { amendments: [], effectiveAmendment: { ...amendment, reviewedByUserId: party } }]) expect(() => decodeCounterpartyHistory({ ...history, ...changes }, client, party)).toThrow();
  });
});
