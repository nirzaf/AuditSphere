import { describe, expect, it } from 'vitest';
import { decodeReadiness } from './lifecycle';
describe('Period lifecycle contract', () => {
  const report = { periodId: '11111111-1111-4111-8111-111111111111', periodCode: '2026', periodStatus: 'DRAFT', canClose: true,
    canDecideClose: false, canReopen: false, packageCount: 0, blockers: [] };
  it('keeps readiness separate from decision authority', () => {
    expect(decodeReadiness(report).canClose).toBe(true);
    expect(decodeReadiness(report).canDecideClose).toBe(false);
  });
  it('rejects missing authority flags and malformed blockers', () => {
    expect(() => decodeReadiness({ ...report, canReopen: undefined })).toThrow();
    expect(() => decodeReadiness({ ...report, blockers: [{ code: 'blocked', detail: 5 }] })).toThrow();
  });
});
