import { describe, expect, it } from 'vitest';
import { decodeAmounts, decodeQuotation } from './quotation';
const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const amounts = {
  baseAmount: '9007199254740993.01',
  complexityAmount: '-1.01',
  riskPremiumAmount: '0',
  discountAmount: '0',
  fee: '9007199254740992.00',
  lines: [
    {
      role: 'Partner',
      activity: 'Audit',
      hours: '10',
      rate: '1000',
      amount: '10000',
      rateCardId: id,
    },
  ],
};
const version = {
  id,
  revision: '1',
  status: 'DRAFT',
  complexity: '1',
  risk: '0',
  discount: '0',
  nonStandardTerms: false,
  note: null,
  validUntil: null,
  approvalsStand: false,
  expired: false,
  amounts,
  rules: [],
};
const workspace = {
  proposalId: id,
  proposalRevision: '1',
  currency: 'QAR',
  editable: true,
  rates: [{ id, role: 'Partner', activity: 'Audit', rate: '1000' }],
  versions: [version],
};
describe('Quotation contracts', () => {
  it('preserves exact signed server amounts', () => {
    expect(decodeAmounts(amounts).baseAmount).toBe('9007199254740993.01');
    expect(decodeQuotation(workspace).versions[0].amounts.complexityAmount).toBe('-1.01');
  });
  it('rejects numeric fee, revision and hours', () => {
    expect(() => decodeAmounts({ ...amounts, fee: 1.2 })).toThrow();
    expect(() => decodeQuotation({ ...workspace, proposalRevision: 1 })).toThrow();
    expect(() =>
      decodeAmounts({ ...amounts, lines: [{ ...amounts.lines[0], hours: 10 }] }),
    ).toThrow();
  });
  it('bounds versions and approved-rate projections', () => {
    expect(() => decodeQuotation({ ...workspace, versions: Array(101).fill(version) })).toThrow();
    expect(() =>
      decodeQuotation({ ...workspace, rates: Array(201).fill(workspace.rates[0]) }),
    ).toThrow();
  });
  it('requires explicit server approval flags and immutable identity', () => {
    const rule = {
      key: 'DEFAULT:DISCOUNT',
      role: 'Partner',
      requirement: 'Discount',
      approved: false,
      canApprove: false,
      approvedBy: null,
      approvedAt: null,
      reason: null,
      approvalId: null,
      canRevoke: false,
      revokedAt: null,
      revocationReason: null,
    };
    expect(
      decodeQuotation({ ...workspace, versions: [{ ...version, rules: [rule] }] }).versions[0]
        .rules[0].canApprove,
    ).toBe(false);
    expect(() =>
      decodeQuotation({
        ...workspace,
        versions: [{ ...version, rules: [{ ...rule, canApprove: 'yes' }] }],
      }),
    ).toThrow();
    // A standing approval always carries its identity; a revocation flag is never inferred client-side.
    const approved = { ...rule, approved: true, approvedBy: id, approvedAt: '2026-10-10T10:00:00+00:00', reason: 'Agreed', approvalId: id, canRevoke: true };
    const decoded = decodeQuotation({ ...workspace, versions: [{ ...version, status: 'APPROVED', approvalsStand: true, rules: [approved] }] });
    expect([decoded.versions[0].approvalsStand, decoded.versions[0].rules[0].canRevoke]).toEqual([true, true]);
    for (const forged of [{ ...approved, approvalId: null }, { ...approved, canRevoke: undefined }, { ...rule, approvalId: id }])
      expect(() => decodeQuotation({ ...workspace, versions: [{ ...version, rules: [forged] }] })).toThrow();
    expect(() => decodeQuotation({ ...workspace, versions: [{ ...version, approvalsStand: undefined }] })).toThrow();
  });
});
