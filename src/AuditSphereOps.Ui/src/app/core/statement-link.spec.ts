import { describe, expect, it } from 'vitest';
import { safeReturnPath, statementLinkContext } from './statement-link';

const revision = 'c'.repeat(64);
const query = (values: Record<string, string>) => ({ get: (name: string) => values[name] ?? null });

describe('statement link context (STE-NXT-013)', () => {
  it('accepts only in-app return paths', () => {
    expect(safeReturnPath('/app/engagements/a/statements')).toBe('/app/engagements/a/statements');
    for (const bad of ['//evil.example/x', 'https://evil.example/', '/app/x\\y', '/app/x\u0007', 'app/engagements', '/app/' + 'x'.repeat(300), null, '']) {
      expect(safeReturnPath(bad)).toBeNull();
    }
  });

  it('reads a complete, well-formed link', () => {
    const link = statementLinkContext(query({ area: 'Revenue', line: 'REV-100', periodStart: '2026-01-01', periodEnd: '2026-12-31', revision, returnUrl: '/app/engagements/a/statements' }));
    expect(link).toEqual({ area: 'Revenue', line: 'REV-100', periodStart: '2026-01-01', periodEnd: '2026-12-31', revision, returnUrl: '/app/engagements/a/statements' });
  });

  it('fills nothing from an incomplete or malformed link', () => {
    expect(statementLinkContext(null)).toBeNull();
    expect(statementLinkContext(query({ area: 'Revenue', line: 'REV-100', periodStart: '2026-01-01', periodEnd: '2026-12-31' }))).toBeNull();
    expect(statementLinkContext(query({ area: 'Revenue', line: 'REV-100', periodStart: '01/01/2026', periodEnd: '2026-12-31', revision }))).toBeNull();
    expect(statementLinkContext(query({ area: 'x'.repeat(81), line: 'REV-100', periodStart: '2026-01-01', periodEnd: '2026-12-31', revision }))).toBeNull();
  });

  it('drops an unsafe return path but keeps the pre-fill', () => {
    const link = statementLinkContext(query({ area: 'Revenue', line: 'REV-100', periodStart: '2026-01-01', periodEnd: '2026-12-31', revision, returnUrl: 'https://evil.example' }));
    expect(link?.returnUrl).toBeNull();
    expect(link?.line).toBe('REV-100');
  });
});
