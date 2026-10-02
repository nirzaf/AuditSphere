import { describe, expect, it } from 'vitest';
import { decodeSearch, migratedHref } from './search';
describe('Authorized navigation search contract', () => {
  it('rejects external and unsafe links', () => {
    for (const href of ['https://example.com', '//example.com/app', '/app\\evil', '/app?token=x']) {
      expect(() =>
        decodeSearch({
          term: 'client',
          truncated: false,
          hits: [{ kind: 'Client', title: 'Client', detail: '', href }],
        }),
      ).toThrow();
    }
  });
  it('keeps ownership explicit', () => {
    expect(migratedHref('/app')).toBe('/ui/app');
    expect(migratedHref('/app/clients/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')).toContain(
      '/ui/app/clients/',
    );
    expect(migratedHref('/app/practice/commercial-settings')).toBe(
      '/ui/app/practice/commercial-settings',
    );
    expect(migratedHref('/app/finance')).toBe('/ui/app/finance');
    expect(migratedHref('/app/engagements/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/pbc')).toBe(
      '/ui/app/engagements/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/pbc',
    );
    expect(migratedHref('/app/practice/invoices/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')).toContain('/ui/app/practice/invoices/');
    expect(migratedHref('/app/library/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')).toContain('/ui/app/library/');
    expect(migratedHref('/app/unowned')).toBeNull();
    expect(migratedHref('/app/clients/' + '-'.repeat(36))).toBeNull();
    expect(migratedHref('/app/clients/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa?returnUrl=https://example.test')).toBeNull();
    expect(migratedHref('//example.test/app')).toBeNull();
  });
});
