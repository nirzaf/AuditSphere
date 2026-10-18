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
const history = {
  id,
  action: 'CLIENT_CONVERSION_LINKED',
  occurredAt: '2026-10-18T10:00:00Z',
  actorName: 'Reviewer',
  actorUserId: id,
  isBackfill: false,
  fromState: 'ACCEPTED',
  toState: 'PROSPECT',
  sourceChannel: null,
  ownerName: null,
  ownerUserId: null,
  serviceRoute: 'Audit',
  opportunityId: id,
  proposalId: id,
  proposalRevision: '1',
  clientId: id,
};
const lead = {
  id,
  name: 'Lead',
  status: 'QUALIFIED',
  source: 'Referral',
  ownerUserId: null,
  ownerName: 'Not assigned',
  createdAt: '2026-10-18T09:00:00Z',
  opportunities: [opportunity],
  history: [history],
  historyHasMore: false,
  hasLegacyHistoryGap: false,
};
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
  it('retains a valid lead conversion identity in its activity history', () => {
    const result = decodeOpportunities(lead);
    expect(result.source).toBe('Referral');
    expect(result.history[0].action).toBe('CLIENT_CONVERSION_LINKED');
    expect(result.history[0].proposalRevision).toBe('1');
    expect(result.history[0].clientId).toBe(id);
  });
  it('rejects unknown activity and malformed conversion identities', () => {
    for (const change of [{ action: 'IMPLIED_APPROVAL' }, { clientId: 'unknown' }])
      expect(() =>
        decodeOpportunities({ ...lead, history: [{ ...history, ...change }] }),
      ).toThrow();
  });
});
