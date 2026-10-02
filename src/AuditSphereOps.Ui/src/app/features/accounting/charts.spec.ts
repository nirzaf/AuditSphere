import { describe, expect, it } from 'vitest';
import { decodeCharts, decodeAccounts, decodePublication } from './charts';
describe('Chart contracts', () => {
  it('requires exact publication snapshot and explicit authority', () => {
    const value = { chartId: '11111111-1111-4111-8111-111111111111', version: '1', digest: 'f'.repeat(64), accountCount: 1, canPublish: false };
    expect(decodePublication(value).canPublish).toBe(false);
    expect(() => decodePublication({ ...value, digest: 'bad' })).toThrow();
    expect(() => decodePublication({ ...value, version: 1 })).toThrow();
  });
  it('keeps empty revisions and account pages explicit', () => {
    expect(decodeCharts({ clientId: '11111111-1111-4111-8111-111111111111', items: [], hasMore: false }).items).toEqual([]);
    expect(decodeAccounts({ items: [], totalCount: 0, page: 1, pageSize: 50 }).totalCount).toBe(0);
  });
  it('rejects invalid identity and unbounded pages', () => {
    expect(() => decodeCharts({ clientId: 'bad', items: [], hasMore: false })).toThrow();
    expect(() => decodeAccounts({ items: [], totalCount: 0, page: 10001, pageSize: 50 })).toThrow();
    expect(() => decodeAccounts({ items: [], totalCount: 0, page: 1, pageSize: 501 })).toThrow();
  });
});
