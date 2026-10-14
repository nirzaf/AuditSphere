import { describe, expect, it } from 'vitest';
import { decodeBuildInputs, decodeBuildStatus, emptyBuildForm, emptyMovement, toBuildRequest, BuildForm } from './financial-package-build';

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
      ['a period', (f) => (f.periodStart = '2026/01/01')],
      ['amounts with more than six decimals', (f) => (f.movements[0].amount = '1.0000001')],
      ['a movement without a section', (f) => (f.movements[0].section = 'OTHER')],
      ['a movement without evidence', (f) => (f.movements[0].sourceReferences = ' , ')],
      ['a zero contribution', (f) => (f.movements[0].allocations[0].signedAmount = '0')],
      ['a malformed source line', (f) => (f.movements[0].allocations[0].generalLedgerLineId = 'not-a-guid')],
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
    expect(() => decodeBuildInputs({
      engagementId: '55555555-5555-4555-8555-555555555555', mappings: [], plans: [], comparatives: [],
      periods: [{ id: '44444444-4444-4444-8444-444444444444', periodCode: 'FY2026', startDate: 'January', endDate: '2026-12-31', currency: 'QAR', basis: 'IFRS' }],
    }, 'inputs')).toThrow();
  });
});

describe('financial package build progress contract', () => {
  it('accepts only a scoped durable status and optional completed package identity', () => {
    const operationId = '55555555-5555-4555-8555-555555555555';
    expect(decodeBuildStatus({ operationId, status: 'PENDING', errorCode: null, packageId: null }, 'status').packageId).toBeNull();
    expect(decodeBuildStatus({ operationId, status: 'COMPLETED', errorCode: null, packageId: mapping }, 'status').packageId).toBe(mapping);
    expect(() => decodeBuildStatus({ operationId, status: 'COMPLETED', errorCode: null, packageId: 'not-a-guid' }, 'status')).toThrow();
  });
});
