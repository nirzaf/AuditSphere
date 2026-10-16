import { decodeReviewPointSnapshot, decodeReviewPointPreview, decodeReviewPointReceipt, reviewPointTargetRevision, sameReviewPointSnapshot } from './review-point-command-contracts';
import { reviewPointTestId as id, reviewPointTestPreview as preview, reviewPointTestReceipt as receipt } from './review-point-command-test-data';

describe('Review point exact target and disposition contracts', () => {
  it('retains large exact target revisions and rejects unsafe legacy numeric values', () => {
    expect(reviewPointTargetRevision('9007199254740993', '')).toBe('9007199254740993');
    expect(reviewPointTargetRevision(7, '')).toBe('7');
    expect(() => reviewPointTargetRevision(9007199254740993, '')).toThrow();
    const p = preview({ requestId: id(50), fields: { action: 'CLEAR' } }); p.before.targetRevision = '9007199254740993';
    const decoded = decodeReviewPointReceipt(receipt(p), '');
    expect(decoded.dispositionEvent.after.targetRevision).toBe('9007199254740993');
    expect(decoded.dispositionEvent.after.dispositionRevision).toBe('12');
  });
  it('accepts equivalent timestamp offsets while retaining sub-millisecond evidence', () => {
    const p = preview({ requestId: id(50), fields: { action: 'CLEAR' } }), r = receipt(p);
    r.dispositionEvent = { ...r.dispositionEvent, before: { ...r.dispositionEvent.before, raisedAt: '2026-10-16T03:00:00.1234567+03:00' },
      after: { ...r.dispositionEvent.after, raisedAt: '2026-10-16T03:00:00.1234567+03:00' } };
    expect(() => decodeReviewPointReceipt(r, '')).not.toThrow();
    expect(sameReviewPointSnapshot(decodeReviewPointSnapshot(r.dispositionEvent.before, ''), decodeReviewPointSnapshot({ ...p.before, raisedAt: '2026-10-16T00:00:00.1234568Z' }, ''))).toBe(false);
  });
  for (const key of ['targetId', 'targetKind', 'targetRevision', 'comment', 'significant', 'raisedByUserId'] as const) {
    it(`rejects mutation of immutable ${key} in a disposition event`, () => {
      const p = preview({ requestId: id(50), fields: { action: 'CLEAR' } }), r = receipt(p);
      const changed = key === 'significant' ? false : key === 'targetRevision' ? '8' : key.endsWith('Id') ? id(98) : 'Changed';
      expect(() => decodeReviewPointReceipt({ ...r, dispositionEvent: { ...r.dispositionEvent, after: { ...r.dispositionEvent.after, [key]: changed } } }, '')).toThrow();
    });
  }
  it('binds preview to own disposition revision and exact action effect', () => {
    const p = preview({ requestId: id(50), fields: { action: 'CLEAR' } });
    expect(() => decodeReviewPointPreview({ ...p, expectedDispositionRevision: p.before.targetRevision }, '')).toThrow();
    expect(() => decodeReviewPointPreview({ ...p, cleared: false }, '')).toThrow();
  });
});
