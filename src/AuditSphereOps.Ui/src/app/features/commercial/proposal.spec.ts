import { describe, expect, it } from 'vitest';
import { decodeProposal, ProposalDetail } from './proposal';
import { signal } from '@angular/core';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
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

describe('proposal refresh draft preservation', () => {
  function workspace(dirty: boolean) {
    return {
      id, request: undefined, data: signal<unknown>(proposal), error: signal(''), loading: signal(false),
      session: { current: () => ({ staff: true }), invalidation: () => 0 },
      http: { get: () => of(proposal) },
      tabDraft: { dirty: () => dirty, reset: vi.fn(), bind: vi.fn() },
      draft: { scope: 'Unsubmitted scope' }, reviewed: true, legalName: '',
    };
  }
  it('retains unsubmitted fields across a persisted action refresh and clears assent', () => {
    const w = workspace(true);
    ProposalDetail.prototype.load.call(w as unknown as ProposalDetail);
    expect(w.draft.scope).toBe('Unsubmitted scope');
    expect(w.reviewed).toBe(false);
    expect(w.tabDraft.reset).not.toHaveBeenCalled();
    expect(w.tabDraft.bind).toHaveBeenCalledWith(`commercial-proposal:${id}`, proposal);
  });
  it('refreshes a clean form from persisted fields', () => {
    const w = workspace(false);
    ProposalDetail.prototype.load.call(w as unknown as ProposalDetail);
    expect(w.draft.scope).toBe(proposal.scope);
    expect(w.tabDraft.reset).toHaveBeenCalledOnce();
  });
  it('clears protected content when a refresh is refused', () => {
    const w = workspace(true);
    w.http.get = () => throwError(() => ({ status: 403 }));
    ProposalDetail.prototype.load.call(w as unknown as ProposalDetail);
    expect(w.data()).toBeNull();
    expect(w.error()).toContain('unavailable');
  });
});
