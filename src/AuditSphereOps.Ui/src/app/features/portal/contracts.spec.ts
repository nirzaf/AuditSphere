import { describe, expect, it } from 'vitest';
import { decode } from '../../core/decode';
import { portalRequest, portalWorkspace, revision } from './contracts';
import { clientPackage } from './package';
describe('client portal contracts', () => {
  it('preserves exact byte/revision strings and refuses numeric or signed values', () => {
    expect(decode(revision, '9223372036854775807')).toBe('9223372036854775807');
    for (const bad of [42, '-1', '1.1', '1e6']) expect(() => decode(revision, bad)).toThrow();
  });
  it('does not accept partial staff DTOs as portal responses', () => {
    expect(() => decode(portalRequest, { id: crypto.randomUUID(), area: 'Private workpaper' })).toThrow();
    expect(() => decode(portalWorkspace, { requests: [], packages: [] })).toThrow();
  });
  it('refuses floating point package totals', () => {
    const sample = { packageId: crypto.randomUUID(), framework: 'IFRS', periodStart: '2026-01-01', periodEnd: '2026-12-31', currency: 'QAR', templateVersion: '1', packageHash: 'a'.repeat(64),
      hasCashFlow: true, hasDisclosures: true, managementDecision: 'PENDING', managementDecidedAt: null, statementTotals: [{ statementSection: 'Assets', amount: '9007199254740993.25' }] };
    expect(decode(clientPackage, sample).statementTotals[0].amount).toBe('9007199254740993.25');
    expect(() => decode(clientPackage, { ...sample, statementTotals: [{ statementSection: 'Assets', amount: 1.25 }] })).toThrow();
  });
});
