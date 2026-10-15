import { describe, expect, it } from 'vitest';
import { contentDecisionIntent, contentDecisionRequest, decodeStatementContentReview } from './content-review';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const snapshot = 'a'.repeat(64);

function state(latestReviewId: string | null = null) {
  return decodeStatementContentReview({
    financialPackageId: id1, presentationRevisionId: id2, snapshotSha256Hex: snapshot, approved: false, canReview: true,
    latestReviewId, reviewedChecks: ['NOTE_CONTENT', 'EQUITY_SOURCE_LINEAGE'], reviews: [],
  }, 'review');
}

describe('statement content review contract (F-03)', () => {
  it('sends the exact snapshot and latest decision the reviewer saw', () => {
    expect(contentDecisionRequest(state(), 'APPROVED', null, 'request-1')).toEqual({
      decision: 'APPROVED', reason: null, requestId: 'request-1', expectedSnapshotSha256Hex: snapshot, expectedPreviousReviewId: null,
    });
  });

  it('keeps the request identity for a retry and renews it when the decision or the chain changes', () => {
    const first = contentDecisionIntent(null, state(), 'APPROVED', null, 'request-1');
    expect(contentDecisionIntent(first, state(), 'APPROVED', null, 'request-2')).toBe(first);
    expect(contentDecisionIntent(first, state(), 'RETURNED', 'Missing lease note', 'request-2').requestId).toBe('request-2');
    expect(contentDecisionIntent(first, state(id1), 'APPROVED', null, 'request-3').requestId).toBe('request-3');
  });

  it('refuses an unknown decision or a malformed snapshot digest', () => {
    expect(() => decodeStatementContentReview({ ...state(), snapshotSha256Hex: 'bad' }, 'review')).toThrow();
    expect(() => decodeStatementContentReview({ ...state(), reviews: [{ id: id1, decision: 'PENDING', reason: '', reviewedByUserId: id2,
      reviewedAt: '2026-10-15T00:00:00Z', snapshotSha256Hex: snapshot }] }, 'review')).toThrow();
  });
});
