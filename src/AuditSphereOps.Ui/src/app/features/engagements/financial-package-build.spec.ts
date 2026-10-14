import { describe, expect, it } from 'vitest';
import { decodeBuildInputs, decodeBuildStatus, emptyBuildForm, emptyMovement, isTerminalBuildStatus, toBuildRequest, BuildForm } from './financial-package-build';

const mapping = '11111111-1111-4111-8111-111111111111';
const plan = '22222222-2222-4222-8222-222222222222';
const line = '33333333-3333-4333-8333-333333333333';

function completeForm(): BuildForm {
  return {
    ...emptyBuildForm(), mappingVersionId: mapping, adjustmentPlanId: plan, periodId: '44444444-4444-4444-8444-444444444444',
    periodStart: '2026-01-01', periodEnd: '2026-12-31', templateVersion: 'build-v1', cashBeginning: '0', cashEnding: '100',
    cashAccountCodes: '1000', openingEvidence: 'opening-bank', closingEvidence: 'closing-bank',
    movements: [{ ...emptyMovement(), description: 'Cash receipts', amount: '100', movementDate: '2026-06-30', activityCode: 'CASH_RECEIPTS',
      sourceReferences: 'gl-1, gl-2', rationale: 'Agreed to ledger.', allocations: [{ generalLedgerLineId: line, signedAmount: '100' }] }],
    disclosures: [{ code: 'CASH_POLICY', response: 'Face value.', notApplicable: false, rationale: '' }],
  };
}

describe('financial package build request', () => {
  it('sends the exact request shape the server binds, with links only where chosen', () => {
    const built = toBuildRequest(completeForm());
    expect('body' in built).toBe(true);
    const body = (built as { body: any }).body;
    expect(body.mappingVersionId).toBe(mapping);
    expect(body.adjustmentPlanId).toBe(plan);
    expect(body.supplementaryInformation.cashAccountCodes).toEqual(['1000']);
    expect(body.supplementaryInformation.cashFlowLines[0].sourceAllocations).toEqual([{ generalLedgerLineId: line, signedAmount: '100' }]);
    expect(body.supplementaryInformation.cashFlowLines[0].amount).toBe('100');
    expect(body.supplementaryInformation.cashBeginning).toBe('0');
    expect(body.supplementaryInformation.comparative).toBeNull();
    expect(body.supplementaryInformation.cashFlowLines[0].sourceReferences).toEqual(['gl-1', 'gl-2']);
    expect(body.supplementaryInformation.equityLines).toBeNull();
  });

  it('keeps a movement without links unlinked, so the server records it as a draft', () => {
    const form = completeForm();
    form.movements[0].allocations = [];
    const body = (toBuildRequest(form) as { body: any }).body;
    expect(body.supplementaryInformation.cashFlowLines[0].sourceAllocations).toBeNull();
  });

  it('preserves exact high-precision amounts and normalizes references without floating-point conversion', () => {
    const form = completeForm();
    form.cashBeginning = '9007199254.123456';
    form.cashEnding = '9007199254.123457';
    form.movements[0].amount = '9007199254.123456';
    form.movements[0].allocations[0].signedAmount = '9007199254.123456';
    form.movements[0].description = '  Cash receipts  ';
    form.movements[0].activityCode = '  CASH_RECEIPTS  ';
    form.movements[0].rationale = '  Agreed to ledger.  ';
    form.movements[0].sourceReferences = ' gl-1, , gl-2 ';
    form.disclosures = [
      { code: '  CASH_POLICY ', response: '  Policy response.  ', notApplicable: false, rationale: 'ignored' },
      { code: '  RELATED_PARTIES ', response: 'ignored', notApplicable: true, rationale: '  None identified.  ' },
    ];

    const body = (toBuildRequest(form) as { body: any }).body;
    const supplementary = body.supplementaryInformation;
    expect(supplementary.cashBeginning).toBe('9007199254.123456');
    expect(supplementary.cashEnding).toBe('9007199254.123457');
    expect(supplementary.cashFlowLines[0]).toMatchObject({
      description: 'Cash receipts', amount: '9007199254.123456', activityCode: 'CASH_RECEIPTS',
      sourceReferences: ['gl-1', 'gl-2'], classificationRationale: 'Agreed to ledger.',
      sourceAllocations: [{ generalLedgerLineId: line, signedAmount: '9007199254.123456' }],
    });
    expect(supplementary.disclosures).toEqual([
      { code: 'CASH_POLICY', response: 'Policy response.', notApplicable: false, rationale: null },
      { code: 'RELATED_PARTIES', response: '', notApplicable: true, rationale: 'None identified.' },
    ]);
  });

  it('preserves comparative package identity and requires its basis and evidence', () => {
    const form = completeForm();
    form.comparativePackageId = '77777777-7777-4777-8777-777777777777';
    form.comparativeBasis = 'Prior audited year';
    form.comparativeEvidence = 'prior signed statement';
    const body = (toBuildRequest(form) as { body: any }).body;
    expect(body.supplementaryInformation.comparative).toEqual({
      packageId: form.comparativePackageId, basis: form.comparativeBasis, evidenceReference: form.comparativeEvidence,
    });
    form.comparativeEvidence = '';
    expect(toBuildRequest(form)).toEqual({ error: 'A comparative package needs a valid package ID, basis, and evidence reference.' });
  });

  it('refuses form mistakes before they reach the worker', () => {
    const cases: [string, (f: BuildForm) => void][] = [
      ['a mapping', (f) => (f.mappingVersionId = '')],
      ['a finalized plan', (f) => (f.adjustmentPlanId = 'not-a-guid')],
      ['a period', (f) => (f.periodStart = '2026/01/01')],
      ['an empty template version', (f) => (f.templateVersion = '  ')],
      ['cash amounts with more than six decimals', (f) => (f.cashBeginning = '1.0000001')],
      ['amounts with more than six decimals', (f) => (f.movements[0].amount = '1.0000001')],
      ['a movement without a section', (f) => (f.movements[0].section = 'OTHER')],
      ['a movement without a description', (f) => (f.movements[0].description = '  ')],
      ['a movement without a date', (f) => (f.movements[0].movementDate = '')],
      ['a movement without an activity code', (f) => (f.movements[0].activityCode = 'has spaces')],
      ['a movement without evidence', (f) => (f.movements[0].sourceReferences = ' , ')],
      ['a movement without rationale', (f) => (f.movements[0].rationale = '  ')],
      ['a zero contribution', (f) => (f.movements[0].allocations[0].signedAmount = '0')],
      ['a malformed source line', (f) => (f.movements[0].allocations[0].generalLedgerLineId = 'not-a-guid')],
      ['a comparative package without a valid identity', (f) => {
        f.comparativePackageId = 'bad'; f.comparativeBasis = 'Prior year'; f.comparativeEvidence = 'prior report';
      }],
      ['a disclosure without a code', (f) => (f.disclosures[0].code = '  ')],
      ['an applicable disclosure without a response', (f) => (f.disclosures[0].response = '  ')],
      ['a not-applicable disclosure without rationale', (f) => {
        f.disclosures[0] = { code: 'COMMITMENTS', response: '', notApplicable: true, rationale: '' };
      }],
    ];
    for (const [label, change] of cases) {
      const form = completeForm();
      change(form);
      expect('error' in toBuildRequest(form), label).toBe(true);
    }
  });

  it('requires at least one movement', () => {
    const form = completeForm();
    form.movements = [];
    expect(toBuildRequest(form)).toEqual({ error: 'Add at least one cash-flow movement.' });
  });
});

describe('financial package build inputs contract', () => {
  it('decodes the choices a preparer can make', () => {
    const decoded = decodeBuildInputs({
      engagementId: '55555555-5555-4555-8555-555555555555',
      mappings: [{ id: mapping, version: 2, datasetId: '66666666-6666-4666-8666-666666666666' }],
      plans: [{ id: plan, baseDatasetId: '66666666-6666-4666-8666-666666666666', resultHash: 'a'.repeat(64) }],
      periods: [{ id: '44444444-4444-4444-8444-444444444444', periodCode: 'FY2026', startDate: '2026-01-01', endDate: '2026-12-31', currency: 'QAR', basis: 'IFRS' }],
      comparatives: [{ id: '77777777-7777-4777-8777-777777777777', framework: 'IFRS', periodStart: '2025-01-01', periodEnd: '2025-12-31', currency: 'QAR', status: 'VALIDATED' }],
    }, 'inputs');
    expect(decoded.mappings[0].version).toBe(2);
    expect(decoded.periods[0].currency).toBe('QAR');
  });

  it('refuses a malformed period date', () => {
    const value = {
      engagementId: '55555555-5555-4555-8555-555555555555', mappings: [], plans: [], comparatives: [],
      periods: [{ id: '44444444-4444-4444-8444-444444444444', periodCode: 'FY2026', startDate: 'January', endDate: '2026-12-31', currency: 'QAR', basis: 'IFRS' }],
    };
    expect(() => decodeBuildInputs(value, 'inputs')).toThrow();
  });

  it('rejects malformed identifiers, counts and nested source choices instead of rendering guessed values', () => {
    const valid = {
      engagementId: '55555555-5555-4555-8555-555555555555',
      mappings: [{ id: mapping, version: 2, datasetId: '66666666-6666-4666-8666-666666666666' }],
      plans: [{ id: plan, baseDatasetId: '66666666-6666-4666-8666-666666666666', resultHash: 'a'.repeat(64) }],
      periods: [{ id: '44444444-4444-4444-8444-444444444444', periodCode: 'FY2026', startDate: '2026-01-01', endDate: '2026-12-31', currency: 'QAR', basis: 'IFRS' }],
      comparatives: [{ id: '77777777-7777-4777-8777-777777777777', framework: 'IFRS', periodStart: '2025-01-01', periodEnd: '2025-12-31', currency: 'QAR', status: 'VALIDATED' }],
    };
    for (const value of [
      null,
      { ...valid, engagementId: 'bad' },
      { ...valid, mappings: [{ ...valid.mappings[0], version: -1 }] },
      { ...valid, plans: [{ ...valid.plans[0], baseDatasetId: 'bad' }] },
      { ...valid, periods: [{ ...valid.periods[0], id: 'bad' }] },
      { ...valid, comparatives: Array(101).fill(valid.comparatives[0]) },
    ]) expect(() => decodeBuildInputs(value, 'inputs')).toThrow();
  });
});

describe('financial package build status contract', () => {
  const operationId = '88888888-8888-4888-8888-888888888888';
  const completed = {
    operationId, status: 'COMPLETED', attemptCount: 1,
    createdAt: '2026-10-14T12:00:00Z', completedAt: '2026-10-14T12:00:03Z',
    packageId: '99999999-9999-4999-8999-999999999999',
  };

  it('decodes a completed operation and package identity', () => {
    expect(decodeBuildStatus(completed, 'build')).toEqual(completed);
  });

  it('stops polling terminal states while leaving cancellation requests active', () => {
    expect(isTerminalBuildStatus('COMPLETED')).toBe(true);
    expect(isTerminalBuildStatus('PROVIDER_BLOCKED')).toBe(true);
    expect(isTerminalBuildStatus('CANCELLED_WITH_DISPOSITION')).toBe(true);
    expect(isTerminalBuildStatus('CANCEL_REQUESTED')).toBe(false);
    expect(isTerminalBuildStatus('RETRY_WAIT')).toBe(false);
  });

  it('rejects unknown states, invalid counters and malformed timestamps or package IDs', () => {
    for (const value of [
      { ...completed, status: 'SUCCESS' },
      { ...completed, attemptCount: -1 },
      { ...completed, createdAt: 'yesterday' },
      { ...completed, completedAt: 'not-a-timestamp' },
      { ...completed, packageId: 'not-a-guid' },
    ]) expect(() => decodeBuildStatus(value, 'build')).toThrow();
  });
});
