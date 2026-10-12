import { describe, expect, it } from 'vitest';
import { decodeLeads, isLeadChannel, LEAD_CHANNELS } from './leads';
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

describe('Lead channels (STE 4.1.1)', () => {
  it('offers exactly the approved channel set', () => {
    expect([...LEAD_CHANNELS]).toEqual(['Phone', 'WhatsApp', 'Email', 'Web Form', 'Referral', 'In-person']);
  });

  it('accepts only approved channels and rejects free text', () => {
    expect(isLeadChannel('Referral')).toBe(true);
    expect(isLeadChannel('Web Form')).toBe(true);
    expect(isLeadChannel('Web')).toBe(false);
    expect(isLeadChannel('web form')).toBe(false);
    expect(isLeadChannel('')).toBe(false);
  });
});
