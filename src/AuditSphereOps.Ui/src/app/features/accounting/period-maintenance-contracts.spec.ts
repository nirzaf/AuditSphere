import { sameMaintenanceIntent, decodeMaintenanceIntent, decodeMaintenanceLookup, decodeMaintenancePreview, decodeMaintenanceReceipt } from './period-maintenance-contracts';

const a = '11111111-1111-4111-8111-111111111111', b = '22222222-2222-4222-8222-222222222222';
const basis = 'a'.repeat(64), hash = 'b'.repeat(64);
const receipt = { id: a, clientId: a, actorId: b, actorEpoch: '9007199254740993', requestId: b, requestHash: hash, action: 'ROLL_FORWARD', reviewBasis: basis,
  createdAt: '2026-10-16T00:00:00Z', result: { periodId: b, restatementId: null, status: 'DRAFT', snapshotJson: 'must not reach product presentation' } };
const intent = { requestId: b, reviewBasis: basis, action: 'ROLL_FORWARD', rollForward: { clientId: a, priorPeriodId: a, periodCode: '2026',
  startDate: '2026-01-01', endDate: '2026-12-31', basis: 'STATUTORY', currency: 'QAR', sourceHash: hash,
  priorClosingAmount: '9999999999999.123456', currentOpeningAmount: '9999999999999.123456', evidenceReference: 'source', sourcePackageId: null, expectedPriorRevision: 2 },
  restatement: null, restatementId: null, reviewed: false, expectedRequestHash: null };
describe('Period maintenance wire contracts', () => {
  it('keeps epochs and financial amounts exact strings', () => {
    expect(decodeMaintenanceReceipt(receipt, '').actorEpoch).toBe('9007199254740993');
    expect(decodeMaintenanceIntent(intent, '').rollForward?.priorClosingAmount).toBe('9999999999999.123456');
    for (const actorEpoch of [9007199254740993, '1.0', '-1', '01']) expect(() => decodeMaintenanceReceipt({ ...receipt, actorEpoch }, '')).toThrow();
    expect(() => decodeMaintenanceIntent({ ...intent, rollForward: { ...intent.rollForward, priorClosingAmount: 0.1 } }, '')).toThrow();
  });
  it('decodes structured canonical preview and discards opaque state and snapshot JSON', () => {
    const preview = decodeMaintenancePreview({ requestId: b, requestHash: hash, canonicalInput: intent, summary: 'Create a draft period and opening bridge.',
      state: { clientId: a, action: 'ROLL_FORWARD', periodId: a, restatementId: null, reviewBasis: basis, canProceed: true, blockers: [], stateJson: 'opaque' } }, '');
    expect(preview.canonicalInput.rollForward?.startDate).toBe('2026-01-01');
    expect('stateJson' in preview.state).toBe(false);
    expect('snapshotJson' in decodeMaintenanceReceipt(receipt, '').result).toBe(false);
  });
  it('refuses unsupported actions, malformed request hashes and incomplete lookup receipts', () => {
    expect(() => decodeMaintenanceIntent({ ...intent, action: 'REOPEN' }, '')).toThrow();
    expect(() => decodeMaintenanceReceipt({ ...receipt, requestHash: 'partial' }, '')).toThrow();
    expect(() => decodeMaintenanceLookup({ found: true, receipt: { requestId: b } }, '')).toThrow();
  });
  it('compares financial values exactly across scale changes without relaxing intent identity', () => {
    for (const [raw, canonical] of [['0', '0.000000'], ['100', '100.000000'], ['-100.12', '-100.120000'], ['0001.20', '1.200000'], ['-0', '0.000000'], ['9999999999999.123456', '9999999999999.123456']]) {
      const left = decodeMaintenanceIntent({ ...intent, rollForward: { ...intent.rollForward, priorClosingAmount: raw } }, '');
      const right = decodeMaintenanceIntent({ ...intent, rollForward: { ...intent.rollForward, priorClosingAmount: canonical } }, '');
      expect(sameMaintenanceIntent(left, right)).toBe(true);
      expect(sameMaintenanceIntent(left, { ...right, requestId: a })).toBe(false);
    }
    const left = decodeMaintenanceIntent(intent, '');
    expect(sameMaintenanceIntent(left, { ...left, rollForward: { ...left.rollForward!, currentOpeningAmount: '9999999999999.123457' } })).toBe(false);
  });
  it('requires approval detail to bind to the selected state and canonical request', () => {
    const detail = { restatementId: b, clientId: a, engagementId: b, periodId: a, periodCode: '2025', periodStart: '2025-01-01', periodEnd: '2025-12-31', currency: 'QAR',
      originalPackageId: a, originalPackageHash: hash, revisedPackageId: b, revisedPackageHash: basis, revisedBasis: 'IFRS', changeType: 'RESTATED_ERROR',
      affectedPeriods: '2025', reason: 'Submitted correction', evidenceReference: 'submitted source', status: 'SUBMITTED', createdByUserId: b, createdAt: '2026-10-16T00:00:00Z' };
    const approval = { requestId: b, requestHash: hash, summary: 'Approve independently', approvalDetail: detail,
      state: { clientId: a, action: 'APPROVE_RESTATEMENT', periodId: a, restatementId: b, reviewBasis: basis, canProceed: true, blockers: [] },
      canonicalInput: { requestId: b, reviewBasis: basis, action: 'APPROVE_RESTATEMENT', restatementId: b, rollForward: null, restatement: null, reviewed: false, expectedRequestHash: null } };
    expect(decodeMaintenancePreview(approval, '').approvalDetail).toEqual(detail);
    for (const invalid of [null, { ...detail, clientId: b }, { ...detail, periodId: b }, { ...detail, restatementId: a },
      { ...detail, originalPackageHash: 'prefix' }, { ...detail, revisedPackageHash: 5 }, { ...detail, changeType: 'UNKNOWN' }, { ...detail, reason: '' }]) {
      expect(() => decodeMaintenancePreview({ ...approval, approvalDetail: invalid }, '')).toThrow();
    }
    expect(() => decodeMaintenancePreview({ ...approval, canonicalInput: { ...approval.canonicalInput, restatementId: a } }, '')).toThrow();
    expect(() => decodeMaintenancePreview({ ...approval, state: { ...approval.state, action: 'ROLL_FORWARD' } }, '')).toThrow();
  });

});
