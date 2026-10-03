import { describe, expect, it } from 'vitest';
import { decodeCharts, decodeAccounts, decodePublication, decodeAliases } from './charts';
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
    expect(decodeAliases([])).toEqual([]);
  });
  it('decodes valid source account aliases', () => {
    const aliases = decodeAliases([{
      id: '11111111-1111-4111-8111-111111111111',
      clientAccountId: '22222222-2222-4222-8222-222222222222',
      accountCode: '1000',
      sourceSystem: 'XERO',
      aliasCode: '100-BANK',
      aliasName: 'Operating Account',
      createdAt: '2026-10-01T00:00:00Z'
    }]);
    expect(aliases.length).toBe(1);
    expect(aliases[0].aliasCode).toBe('100-BANK');
    expect(aliases[0].sourceSystem).toBe('XERO');
  });
  it('rejects invalid identity and unbounded pages', () => {
    expect(() => decodeCharts({ clientId: 'bad', items: [], hasMore: false })).toThrow();
    expect(() => decodeAccounts({ items: [], totalCount: 0, page: 10001, pageSize: 50 })).toThrow();
    expect(() => decodeAccounts({ items: [], totalCount: 0, page: 1, pageSize: 501 })).toThrow();
    expect(() => decodeAliases([{ id: 'bad', clientAccountId: '22222222-2222-4222-8222-222222222222', accountCode: '1000', sourceSystem: 'XERO', aliasCode: '100', aliasName: '', createdAt: '' }])).toThrow();
  });
});
