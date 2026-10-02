import { describe, expect, it } from 'vitest';
import { decodeFeeAgreement } from './fee-agreement';
const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const milestone = {
  id,
  kind: 'ADVANCE',
  amount: '12500.00',
  state: 'PENDING',
  invoiceId: null,
  invoiceNumber: null,
  invoiceStatus: null,
  allocated: '0',
  outstanding: '12500.00',
  paidAt: null,
};
const workspace = {
  proposalId: id,
  agreementId: id,
  clientId: id,
  engagementId: null,
  fee: '25000.00',
  currency: 'QAR',
  advancePercent: '50',
  canCreate: false,
  canFinance: false,
  releaseRecorded: false,
  creationBlockers: [],
  milestones: [milestone, { ...milestone, kind: 'BALANCE' }],
  engagements: [],
};
describe('Fee agreement contract', () => {
  it('preserves exact amounts and server finance authority', () => {
    const w = decodeFeeAgreement(workspace);
    expect(w.fee).toBe('25000.00');
    expect(w.advancePercent).toBe('50');
    expect(w.canFinance).toBe(false);
  });
  it('rejects numeric money and forged references', () => {
    expect(() => decodeFeeAgreement({ ...workspace, fee: 25000 })).toThrow();
    expect(() =>
      decodeFeeAgreement({
        ...workspace,
        milestones: [{ ...milestone, invoiceId: 'guess' }, workspace.milestones[1]],
      }),
    ).toThrow();
    expect(() => decodeFeeAgreement({ ...workspace, canFinance: 'yes' })).toThrow();
  });
  it('requires both exact milestone kinds and bounds candidates', () => {
    expect(() =>
      decodeFeeAgreement({ ...workspace, milestones: [milestone, milestone] }),
    ).toThrow();
    expect(() => decodeFeeAgreement({ ...workspace, milestones: [] })).toThrow();
    expect(() =>
      decodeFeeAgreement({
        ...workspace,
        engagements: Array(101).fill({ id, serviceRoute: 'Audit', periodStart: '', periodEnd: '' }),
      }),
    ).toThrow();
  });
  it('supports a guarded pre-agreement state', () => {
    expect(
      decodeFeeAgreement({
        ...workspace,
        agreementId: null,
        milestones: [],
        creationBlockers: ['Client acceptance required.'],
      }).creationBlockers,
    ).toHaveLength(1);
  });
});
