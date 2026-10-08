import { decodeReconciliationState } from './analysis-preparation-contracts';
import { decode } from '../../core/decode';
import { reconciliationEditableFields } from './reconciliation-create';

const id = '11111111-1111-4111-8111-111111111111';
const state = {
  engagementId: id, clientId: id, clientName: 'Scoped client', engagementName: 'Scoped audit',
  periodId: id, periodCode: 'FY26', bookId: null, currency: 'QAR', basis: 'IFRS',
  sourceKind: 'TRIAL_BALANCE', sourceId: id, sourceHash: 'a'.repeat(64), inputGeneration: 7,
  reviewBasis: 'b'.repeat(64), canPrepare: true, blockers: [],
};

describe('accounting preparation API contracts', () => {
  it('decodes server generations as integers while keeping digests strict', () => {
    expect(decode(decodeReconciliationState, state).inputGeneration).toBe(7);
    expect(() => decode(decodeReconciliationState, { ...state, inputGeneration: '7' })).toThrow();
    expect(() => decode(decodeReconciliationState, { ...state, sourceHash: 'not-a-digest' })).toThrow();
  });

  it('accepts only the bounded editable reconciliation fields for tab recovery', () => {
    const fields = { area: 'CASH', accountCodes: '1000', asOfDate: '2026-10-04', agingBasis: '',
      agingBucketRuleVersion: '', reason: 'Synthetic preparation', evidenceReference: 'SYNTHETIC-REF' };
    expect(reconciliationEditableFields(fields)).toEqual(fields);
    expect(reconciliationEditableFields({ ...fields, reviewed: true })).toBeNull();
  });
});
