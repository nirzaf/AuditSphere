import { describe, expect, it } from 'vitest';
import { decodeOpportunities } from './opportunities';
const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const opportunity = {
  id,
  serviceRoute: 'Audit',
  entityScope: 'Entity',
  stage: 'DISCOVERY',
  expectedFee: '9007199254740993.01',
  currency: 'QAR',
  periodStart: '2026-01-01',
  periodEnd: '2026-12-31',
  proposalId: null,
  revision: '0',
};
const lead = { id, name: 'Lead', status: 'QUALIFIED', opportunities: [opportunity] };
describe('Opportunity contract', () => {
  it('preserves exact fee and initial revision', () => {
    const result = decodeOpportunities(lead).opportunities[0];
    expect(result.expectedFee).toBe('9007199254740993.01');
    expect(result.revision).toBe('0');
  });
  it('rejects numeric money, revisions and invalid identities', () => {
    for (const change of [{ expectedFee: 1.2 }, { revision: 0 }, { proposalId: 'guess' }])
      expect(() =>
        decodeOpportunities({ ...lead, opportunities: [{ ...opportunity, ...change }] }),
      ).toThrow();
  });
  it('bounds the opportunity projection', () => {
    expect(() =>
      decodeOpportunities({ ...lead, opportunities: Array(101).fill(opportunity) }),
    ).toThrow();
  });
});
