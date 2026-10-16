// Synthetic review point contracts shared only by specs.
import { ReviewPointAction, decodeReviewPointFields } from './review-point-command-contracts';
export const reviewPointTestId = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
export const reviewPointTestBasis = 'b'.repeat(64), reviewPointTestHash = 'a'.repeat(64);
export const reviewPointTestSnapshot = (action: ReviewPointAction = 'CLEAR', n = 1) => ({ reviewPointId: reviewPointTestId(n), clientId: reviewPointTestId(21), engagementId: reviewPointTestId(20),
  dispositionRevision: '11', targetKind: 'WORKPAPER', targetId: reviewPointTestId(30), targetRevision: '7', raisedByUserId: reviewPointTestId(89),
  raisedAt: '2026-10-16T00:00:00.1234567Z', significant: true, cleared: action === 'REOPEN', comment: 'Synthetic original review comment', status: action === 'REOPEN' ? 'CLEARED' : 'BLOCKING' });
export const reviewPointTestState = (action: ReviewPointAction = 'CLEAR', n = 1) => ({ reviewPoint: reviewPointTestSnapshot(action, n), reviewBasis: reviewPointTestBasis,
  canChangeDisposition: true, blockers: [] });
export const reviewPointTestPreview = (request: { requestId: string; fields: unknown }, n = 1) => {
  const fields = decodeReviewPointFields(request.fields, '');
  return { requestId: request.requestId, requestHash: reviewPointTestHash, expectedDispositionRevision: '11', reviewBasis: reviewPointTestBasis,
    canonicalInput: fields, before: reviewPointTestSnapshot(fields.action, n), nextDispositionRevision: '12', cleared: fields.action === 'CLEAR', canProceed: true, blockers: [], summary: 'Exact administrative disposition' };
};
export const reviewPointTestReceipt = (p: ReturnType<typeof reviewPointTestPreview>, actorId = reviewPointTestId(90), actorEpoch = '1') => ({
  id: reviewPointTestId(40), reviewPointId: p.before.reviewPointId, actorId, actorEpoch, requestId: p.requestId, requestHash: p.requestHash,
  reviewBasis: p.reviewBasis, action: p.canonicalInput.action, createdAt: '2026-10-16T01:00:00Z', preview: p,
  dispositionEvent: { id: reviewPointTestId(41), actorId, actorEpoch, action: p.canonicalInput.action, createdAt: '2026-10-16T01:00:00Z', before: p.before,
    after: { ...p.before, dispositionRevision: '12', cleared: p.cleared, status: p.cleared ? 'CLEARED' : p.before.significant ? 'BLOCKING' : 'OPEN' } }
});
export const reviewPointTestRecord = (action: ReviewPointAction = 'CLEAR', n = 1) => {
  const s = reviewPointTestSnapshot(action, n);
  return { id: s.reviewPointId, engagementId: s.engagementId, targetKind: s.targetKind, targetId: s.targetId, targetRevision: '7',
    raisedByUserId: s.raisedByUserId, raisedAt: s.raisedAt, significant: s.significant, cleared: s.cleared, comment: s.comment, status: s.status,
    dispositionRevision: s.dispositionRevision, canChangeDisposition: true };
};
