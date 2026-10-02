import { describe, expect, it } from 'vitest';
import { decodeProposal } from './proposal';
const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const proposal = {
  id,
  opportunityId: id,
  leadName: 'Lead',
  serviceRoute: 'Audit',
  entityScope: 'Entity',
  stage: 'PROPOSAL',
  revision: '1',
  status: 'DRAFT',
  serviceProfile: 'Standard',
  scope: 'Scope',
  exclusions: '',
  deliverables: 'Report',
  dependencies: '',
  fee: '9007199254740993.01',
  currency: 'QAR',
  periodStart: '2026-01-01',
  periodEnd: '2026-12-31',
  responseReason: null,
  clientId: null,
  canApprove: false,
  versions: [],
};
describe('Commercial proposal contract', () => {
  it('preserves fee precision', () => {
    expect(decodeProposal(proposal).fee).toBe('9007199254740993.01');
  });
  it('rejects floating point fee or numeric revision', () => {
    expect(() => decodeProposal({ ...proposal, fee: 123.45 })).toThrow();
    expect(() => decodeProposal({ ...proposal, revision: 1 })).toThrow();
  });
});
