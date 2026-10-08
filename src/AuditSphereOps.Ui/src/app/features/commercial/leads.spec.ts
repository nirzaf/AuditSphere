import { describe, expect, it } from 'vitest';
import { decodeLeads } from './leads';
describe('Commercial lead contract', () => {
  it('accepts bounded empty result', () => {
    expect(decodeLeads({ items: [], total: 0, page: 0, pageSize: 25 }).total).toBe(0);
  });
  it('rejects unbounded page and malformed identity', () => {
    expect(() => decodeLeads({ items: [], total: 0, page: 0, pageSize: 101 })).toThrow();
    expect(() =>
      decodeLeads({ items: [{ id: 'email@example.test' }], total: 1, page: 0, pageSize: 25 }),
    ).toThrow();
  });
});
