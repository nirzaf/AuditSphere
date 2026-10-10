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
  sentOfferSha256: null,
  dispatchState: null,
  dispatchedAt: null,
  dispatchRecipient: null,
  responseOfferSha256: null,
  respondentName: null,
  respondentEmail: null,
  responseEvidenceReference: null,
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

describe('client response binding', () => {
  const offerSha = 'a'.repeat(64);
  function responseWorkspace(overrides: Record<string, unknown> = {}) {
    const sent = { ...proposal, status: 'SENT', sentOfferSha256: offerSha };
    const actions: Array<{ path: string; body: object }> = [];
    const w = {
      data: signal<unknown>(sent), reviewed: true, busy: signal(false), uncertain: signal(false),
      respondentName: 'A. Owner', respondentEmail: 'owner@example.test', evidenceReference: 'Signed letter',
      action: (path: string, body: object) => { actions.push({ path, body }); },
      ...overrides,
    };
    return { w, actions };
  }
  it('sends the dispatched offer identity and respondent evidence with an acceptance', () => {
    const { w, actions } = responseWorkspace();
    ProposalDetail.prototype.recordResponse.call(w as unknown as ProposalDetail, 'ACCEPTED');
    expect(actions).toHaveLength(1);
    expect(actions[0].path).toBe('response');
    expect(actions[0].body).toMatchObject({
      decision: 'ACCEPTED', offerSha256: offerSha, respondentName: 'A. Owner',
      respondentEmail: 'owner@example.test', evidenceReference: 'Signed letter',
    });
  });
  it('refuses acceptance without a respondent or a dispatched offer identity', () => {
    const noRespondent = responseWorkspace({ respondentName: '   ' });
    ProposalDetail.prototype.recordResponse.call(noRespondent.w as unknown as ProposalDetail, 'ACCEPTED');
    expect(noRespondent.actions).toHaveLength(0);
    const noEmail = responseWorkspace({ respondentEmail: '  ' });
    ProposalDetail.prototype.recordResponse.call(noEmail.w as unknown as ProposalDetail, 'ACCEPTED');
    expect(noEmail.actions).toHaveLength(0);
    const noOffer = responseWorkspace({ data: signal({ ...proposal, status: 'SENT', sentOfferSha256: null }) });
    ProposalDetail.prototype.recordResponse.call(noOffer.w as unknown as ProposalDetail, 'ACCEPTED');
    expect(noOffer.actions).toHaveLength(0);
    const noAssent = responseWorkspace({ reviewed: false });
    ProposalDetail.prototype.recordResponse.call(noAssent.w as unknown as ProposalDetail, 'ACCEPTED');
    expect(noAssent.actions).toHaveLength(0);
  });
  it('keeps the decline path working without a respondent name', () => {
    const decline = responseWorkspace({ respondentName: '', respondentEmail: '', evidenceReference: '' });
    ProposalDetail.prototype.recordResponse.call(decline.w as unknown as ProposalDetail, 'DECLINED');
    expect(decline.actions).toHaveLength(1);
    expect(decline.actions[0].body).toMatchObject({ decision: 'DECLINED', offerSha256: offerSha, respondentName: null });
  });
});

describe('proposal refresh draft preservation', () => {
  function workspace(dirty: boolean) {
    return {
      id, request: undefined, data: signal<unknown>(proposal), error: signal(''), loading: signal(false),
      session: { current: () => ({ staff: true }), invalidation: () => 0 },
      http: { get: () => of(proposal) },
      drafts: { load: () => null, save: vi.fn(), clear: vi.fn() },
      uncertain: signal(false), revisionCreatePending: signal(false), recoverableRevisionCreate: signal(false),
      commandStatus: signal(''),
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
