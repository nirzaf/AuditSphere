import { describe, expect, it } from 'vitest';
import { decodeAdvanced } from './advanced';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';

describe('Advanced Consolidation Contracts', () => {
  it('decodes a valid advanced consolidation workspace payload', () => {
    const raw = {
      scope: {
        id: id1,
        groupName: 'Gulf Holding Group',
        version: 1,
        groupRevision: 2,
        method: 'ACQUISITION_NCI',
        reportingCurrency: 'QAR',
        status: 'Draft',
        approvedComponentCount: 2,
        approvedReviewedJournalCount: 1,
      },
      schedules: [
        {
          id: id2,
          status: 'SUBMITTED',
          createdByUserId: id3,
          approvedByUserId: null,
          createdAt: '2026-10-01T10:00:00Z',
          approvedAt: null,
          inputDigest: 'abc123def456',
        },
      ],
      executions: [
        {
          id: id3,
          status: 'VERIFIED',
          createdByUserId: id1,
          approvedByUserId: null,
          createdAt: '2026-10-02T12:00:00Z',
          approvedAt: null,
          comparativeSignedTotal: '1000000.00',
          currentSignedTotal: '1250000.50',
          outputDigest: 'out789xyz012',
        },
      ],
      canPrepare: true,
      canReview: false,
      suggestedSourceManifestJson: '{"sources":[]}',
    };

    const decoded = decodeAdvanced(raw, 'advanced');
    expect(decoded.scope.id).toBe(id1);
    expect(decoded.scope.groupName).toBe('Gulf Holding Group');
    expect(decoded.scope.method).toBe('ACQUISITION_NCI');
    expect(decoded.schedules.length).toBe(1);
    expect(decoded.schedules[0].id).toBe(id2);
    expect(decoded.schedules[0].approvedByUserId).toBeNull();
    expect(decoded.executions.length).toBe(1);
    expect(decoded.executions[0].comparativeSignedTotal).toBe('1000000.00');
    expect(decoded.canPrepare).toBe(true);
    expect(decoded.canReview).toBe(false);
  });

  it('rejects invalid or incomplete payloads', () => {
    expect(() => decodeAdvanced(null, 'advanced')).toThrow();
    expect(() => decodeAdvanced({}, 'advanced')).toThrow();
    expect(() =>
      decodeAdvanced(
        {
          scope: { id: 'invalid-guid' },
        },
        'advanced'
      )
    ).toThrow();
  });
});
