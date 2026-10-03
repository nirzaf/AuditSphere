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
  ownerName: 'Owner',
  authorName: 'Author',
  reviewerName: 'Reviewer',
  approvedAt: '2026-03-01T09:30:00+00:00',
  sentAt: null,
  responseAt: null,
  supersedesId: null,
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
  it('requires read-model names and bounded optional timestamps', () => {
    expect(decodeProposal(proposal).ownerName).toBe('Owner');
    expect(() => decodeProposal({ ...proposal, ownerName: null })).toThrow();
    expect(() => decodeProposal({ ...proposal, supersedesId: 'not-a-guid' })).toThrow();
    expect(decodeProposal({ ...proposal, supersedesId: id }).supersedesId).toBe(id);
  });
  it('rejects version rows missing sent or response timestamps', () => {
    expect(() => decodeProposal({ ...proposal, versions: [{ id, revision: '1', status: 'DRAFT', fee: '1.00', currency: 'QAR', createdAt: '2026-03-01T09:30:00+00:00' }] })).toThrow();
    const ok = decodeProposal({ ...proposal, versions: [{ id, revision: '1', status: 'DRAFT', fee: '1.00', currency: 'QAR', createdAt: '2026-03-01T09:30:00+00:00', sentAt: null, responseAt: null }] });
    expect(ok.versions[0].sentAt).toBeNull();
  });
});
