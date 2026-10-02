import { decodePortfolio } from './portfolio';
describe('portfolio contract', () => {
  it('decodes bounded exact-count rows', () => {
    expect(
      decodePortfolio({
        items: [{ id: '00000000-0000-0000-0000-000000000001', name: 'Client A', engagements: 1 }],
        total: 1,
        page: 0,
        pageSize: 25,
      }).total,
    ).toBe(1);
  });
  it('refuses unsupported counts and identities', () => {
    expect(() => decodePortfolio({items: [], total: 0, page: 0, pageSize: 0})).toThrow();
    expect(() => decodePortfolio({ items: [], total: '1', page: 0, pageSize: 25 })).toThrow();
    expect(() =>
      decodePortfolio({
        items: [{ id: 'email@example.test', name: 'Client', engagements: 1 }],
        total: 1,
        page: 0,
        pageSize: 25,
      }),
    ).toThrow();
  });
});
