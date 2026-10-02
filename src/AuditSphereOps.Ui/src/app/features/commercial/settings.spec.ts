import { describe, expect, it } from 'vitest';
import { decodeSettings } from './settings';
const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const rule = {
  id,
  version: '1',
  kind: 'DISCOUNT_OVER_PERCENT',
  threshold: '5.25',
  role: 'Manager',
};
const workspace = {
  canEdit: false,
  profile: null,
  rulesRevision: 'a'.repeat(64),
  defaultDiscountThreshold: '10',
  defaultRole: 'Partner',
  approverRoles: ['Partner', 'Manager', 'Administrator'],
  rules: [rule],
};
describe('Commercial settings contract', () => {
  it('retains read-only authority, exact threshold and matrix fingerprint', () => {
    const w = decodeSettings(workspace);
    expect(w.canEdit).toBe(false);
    expect(w.rules[0].threshold).toBe('5.25');
    expect(w.rulesRevision).toHaveLength(64);
  });
  it('rejects numeric thresholds/revisions and forged fingerprints', () => {
    expect(() => decodeSettings({ ...workspace, rules: [{ ...rule, threshold: 5.25 }] })).toThrow();
    expect(() => decodeSettings({ ...workspace, rules: [{ ...rule, version: 1 }] })).toThrow();
    expect(() => decodeSettings({ ...workspace, rulesRevision: 'short' })).toThrow();
  });
  it('bounds the active matrix', () => {
    expect(() => decodeSettings({ ...workspace, rules: Array(101).fill(rule) })).toThrow();
  });
});
