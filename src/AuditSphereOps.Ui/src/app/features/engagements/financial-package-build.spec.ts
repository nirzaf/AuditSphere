import { describe, expect, it } from 'vitest';
import {
  decodeBuildInputs, decodeBuildStatus, emptyBuildForm, emptyFxCashEffect, emptyEquityLine, emptyMovement, emptyNoteLine,
  formatMicro, isTerminalBuildStatus, movementProjection, queueButtonLabel, scheduleBlockers, toBuildRequest, BuildForm,
} from './financial-package-build';

const mapping = '11111111-1111-4111-8111-111111111111';
const plan = '22222222-2222-4222-8222-222222222222';
const line = '33333333-3333-4333-8333-333333333333';

function completeForm(): BuildForm {
  return {
    ...emptyBuildForm(), mappingVersionId: mapping, adjustmentPlanId: plan, periodId: '44444444-4444-4444-8444-444444444444',
    periodStart: '2026-01-01', periodEnd: '2026-12-31', templateVersion: 'build-v1', cashBeginning: '0', cashEnding: '100',
    cashAccountCodes: '1000', openingEvidence: 'opening-bank', closingEvidence: 'closing-bank',
    movements: [{ ...emptyMovement(), description: 'Cash receipts', amount: '100', movementDate: '2026-06-30', activityCode: 'CASH_RECEIPTS',
      sourceReferences: 'gl-1, gl-2', rationale: 'Agreed to ledger.', allocations: [{ generalLedgerLineId: line, signedAmount: '-100' }] }],
    disclosures: [{ code: 'CASH_POLICY', response: 'Face value.', notApplicable: false, rationale: '' }],
  };
}

/** Every schedule supplied and linked, so the screen lists no blockers. */
function fullForm(): BuildForm {
  return {
    ...completeForm(),
    equityLines: [{ ...emptyEquityLine(), lineCode: 'SHARE-CAPITAL', description: '  Share capital  ', openingAmount: '1000.000000',
      profitOrLossAmount: '250.5', ociAmount: '-0.000001', capitalMovementAmount: '0', dividendsAmount: '100',
      closingAmount: '1150.499999', evidenceReference: ' equity statement ' }],
    noteLines: [{ ...emptyNoteLine(), noteCode: '  rev-1 ', faceDestinationCode: 'REV', amount: '-12.500000', evidenceReference: 'revenue note' }],
    fxCashEffects: [{ ...emptyFxCashEffect(), currencyPair: ' QAR/USD ', amount: '0.250000', evidenceReference: ' rate table ' }],
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
    expect(body.supplementaryInformation.cashFlowLines[0].sourceAllocations).toEqual([{ generalLedgerLineId: line, signedAmount: '-100' }]);
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
    form.movements[0].allocations[0].signedAmount = '-9007199254.123456';
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
      sourceAllocations: [{ generalLedgerLineId: line, signedAmount: '-9007199254.123456' }],
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

describe('equity, numeric note and FX schedules', () => {
  it('serializes entered rows to the server field names with exact decimal strings', () => {
    const body = (toBuildRequest(fullForm()) as { body: any }).body.supplementaryInformation;
    expect(body.equityLines).toEqual([{
      lineCode: 'SHARE-CAPITAL', description: 'Share capital', openingAmount: '1000.000000', profitOrLossAmount: '250.5',
      ociAmount: '-0.000001', capitalMovementAmount: '0', dividendsAmount: '100', closingAmount: '1150.499999',
      evidenceReference: 'equity statement',
    }]);
    expect(body.noteLines).toEqual([{ noteCode: 'rev-1', faceDestinationCode: 'REV', amount: '-12.500000', evidenceReference: 'revenue note' }]);
    expect(body.fxCashEffects).toEqual([{ currencyPair: 'QAR/USD', amount: '0.250000', evidenceReference: 'rate table' }]);
  });

  it('never sends null for a schedule the preparer entered, and sends null only when no rows exist', () => {
    const entered = (toBuildRequest(fullForm()) as { body: any }).body.supplementaryInformation;
    expect(Array.isArray(entered.equityLines)).toBe(true);
    expect(Array.isArray(entered.noteLines)).toBe(true);
    expect(Array.isArray(entered.fxCashEffects)).toBe(true);

    const none = (toBuildRequest(completeForm()) as { body: any }).body.supplementaryInformation;
    expect(none.equityLines).toBeNull();
    expect(none.noteLines).toBeNull();
    expect(none.fxCashEffects).toBeNull();
  });

  it('refuses each malformed equity amount before it reaches the worker', () => {
    const fields = ['openingAmount', 'profitOrLossAmount', 'ociAmount', 'capitalMovementAmount', 'dividendsAmount', 'closingAmount'] as const;
    for (const field of fields) {
      for (const bad of ['1.0000001', '1,000', '1e3', ' 1', '', '+1', 'abc']) {
        const form = fullForm();
        form.equityLines[0][field] = bad;
        expect('error' in toBuildRequest(form), `${field}=${JSON.stringify(bad)}`).toBe(true);
      }
    }
  });

  it('refuses malformed, duplicate or unbounded schedule rows and a build without disclosures', () => {
    const cases: [string, (f: BuildForm) => void][] = [
      ['an equity line without a code', (f) => (f.equityLines[0].lineCode = '  ')],
      ['equity line codes that repeat ignoring case', (f) => { f.equityLines.push({ ...f.equityLines[0], lineCode: 'share-capital' }); }],
      ['an equity line without a description', (f) => (f.equityLines[0].description = ' ')],
      ['an equity line without evidence', (f) => (f.equityLines[0].evidenceReference = '')],
      ['a note line without a code', (f) => (f.noteLines[0].noteCode = '')],
      ['a note line without a face destination', (f) => (f.noteLines[0].faceDestinationCode = '')],
      ['a note code and face pair that repeats ignoring case', (f) => { f.noteLines.push({ ...f.noteLines[0], noteCode: 'REV-1', faceDestinationCode: 'rev' }); }],
      ['a note amount with more than six decimals', (f) => (f.noteLines[0].amount = '1.0000001')],
      ['a note without evidence', (f) => (f.noteLines[0].evidenceReference = '')],
      ['an FX pair longer than twenty characters', (f) => (f.fxCashEffects[0].currencyPair = 'X'.repeat(21))],
      ['an FX effect without a pair', (f) => (f.fxCashEffects[0].currencyPair = ' ')],
      ['an FX amount that is not a decimal', (f) => (f.fxCashEffects[0].amount = 'abc')],
      ['an FX effect without evidence', (f) => (f.fxCashEffects[0].evidenceReference = ' ')],
      ['a movement whose source contributions exceed it', (f) => (f.movements[0].allocations[0].signedAmount = '-150')],
      ['a movement whose source contributions run against its sign', (f) => (f.movements[0].allocations[0].signedAmount = '100')],
      ['a noncash movement whose ledger contributions run against its sign', (f) => {
        f.movements[0].isNonCash = true; f.movements[0].allocations[0].signedAmount = '-100';
      }],
      ['a source line repeated within one movement', (f) => {
        f.movements[0].allocations.push({ generalLedgerLineId: line.toUpperCase(), signedAmount: '1' });
      }],
      ['a build without disclosures', (f) => (f.disclosures = [])],
    ];
    for (const [label, change] of cases) {
      const form = fullForm();
      change(form);
      expect('error' in toBuildRequest(form), label).toBe(true);
    }
  });

  it('accepts source contributions split across lines that total the movement exactly', () => {
    const form = fullForm();
    form.movements[0].amount = '10.000001';
    form.movements[0].allocations = [
      { generalLedgerLineId: line, signedAmount: '-5.000001' },
      { generalLedgerLineId: '44444444-4444-4444-8444-444444444445', signedAmount: '-5' },
    ];
    expect('body' in toBuildRequest(form)).toBe(true);
    expect(scheduleBlockers(form).some((b) => b.startsWith('Cash-flow source lineage'))).toBe(false);
  });
});

describe('cash-flow contribution projection (F-01)', () => {
  // Each row is the accepted noncash counterpart of a balanced cash journal, with its debit-positive ledger sign unchanged.
  const ordinary: [string, string, string, string][] = [
    ['equipment purchased with bank cash', 'INVESTING', '25000', '-25000'],
    ['receivable collected from a customer', 'OPERATING', '-270000', '270000'],
    ['new borrowing received', 'FINANCING', '-40000', '40000'],
    ['loan principal repaid', 'FINANCING', '10000', '-10000'],
  ];

  for (const [label, section, ledger, effect] of ordinary) {
    it(`accepts the ${label} counterpart and sends its ledger amount unchanged`, () => {
      const form = fullForm();
      form.movements[0] = { ...form.movements[0], section, amount: effect, allocations: [{ generalLedgerLineId: line, signedAmount: ledger }] };
      const built = toBuildRequest(form);
      expect('body' in built, label).toBe(true);
      const sent = (built as { body: any }).body.supplementaryInformation.cashFlowLines[0];
      expect(sent.sourceAllocations).toEqual([{ generalLedgerLineId: line, signedAmount: ledger }]);
      expect(sent.amount).toBe(effect);
      expect(movementProjection(form.movements[0])).toBe(effect);
      expect(scheduleBlockers(form).some((b) => b.startsWith('Cash-flow source lineage'))).toBe(false);
    });
  }

  it('keeps the ledger sign for a noncash movement and refuses the reversed sign', () => {
    const form = fullForm();
    form.movements[0] = { ...form.movements[0], section: 'OPERATING', isNonCash: true, amount: '1200',
      allocations: [{ generalLedgerLineId: line, signedAmount: '1200' }] };
    expect('body' in toBuildRequest(form)).toBe(true);
    expect(movementProjection(form.movements[0])).toBe('1200');
    form.movements[0].allocations[0].signedAmount = '-1200';
    expect(toBuildRequest(form)).toEqual({ error: 'Movement 1: source contributions exceed the movement amount or run against its sign.' });
  });

  it('still refuses over-allocation of a cash counterpart and leaves a partial allocation a draft', () => {
    const form = fullForm();
    form.movements[0] = { ...form.movements[0], section: 'INVESTING', amount: '-25000',
      allocations: [{ generalLedgerLineId: line, signedAmount: '25000.000001' }] };
    expect('error' in toBuildRequest(form)).toBe(true);
    form.movements[0].allocations[0].signedAmount = '20000';
    expect('body' in toBuildRequest(form)).toBe(true);
    expect(scheduleBlockers(form).some((b) => b.startsWith('Cash-flow source lineage: movement 1 has linked lines that do not total'))).toBe(true);
  });

  it('projects compound journals with six-decimal amounts exactly', () => {
    const form = fullForm();
    form.movements[0] = { ...form.movements[0], section: 'OPERATING', amount: '270000.000003', allocations: [
      { generalLedgerLineId: line, signedAmount: '-270000.000004' },
      { generalLedgerLineId: '44444444-4444-4444-8444-444444444445', signedAmount: '0.000001' },
    ] };
    expect('body' in toBuildRequest(form)).toBe(true);
    expect(movementProjection(form.movements[0])).toBe('270000.000003');
    expect(formatMicro(-1n)).toBe('-0.000001');
  });

  it('shows no projection while a link amount is not yet valid', () => {
    const form = fullForm();
    form.movements[0].allocations[0].signedAmount = '12,5';
    expect(movementProjection(form.movements[0])).toBeNull();
  });
});

describe('schedule blockers', () => {
  it('lists each schedule the screen has not yet supplied, and nothing it has', () => {
    const open = scheduleBlockers(completeForm());
    expect(open.some((b) => b.startsWith('Equity rollforward'))).toBe(true);
    expect(open.some((b) => b.startsWith('Numeric note lines'))).toBe(true);
    expect(open.some((b) => b.startsWith('FX cash effect'))).toBe(true);
    expect(open.some((b) => b.startsWith('Disclosures'))).toBe(false);
    expect(open.some((b) => b.startsWith('Cash-flow source lineage'))).toBe(false);
    expect(open).toHaveLength(3);
  });

  it('names movements that are not linked to accepted lines, or whose links do not total them', () => {
    const form = fullForm();
    form.movements.push({ ...emptyMovement(), description: 'Payments', amount: '40', movementDate: '2026-06-30', activityCode: 'PAYMENTS',
      sourceReferences: 'gl-3', rationale: 'Unlinked draft.', allocations: [] });
    form.movements.push({ ...emptyMovement(), description: 'Receipts', amount: '50', movementDate: '2026-06-30', activityCode: 'RECEIPTS',
      sourceReferences: 'gl-4', rationale: 'Partly linked.', allocations: [{ generalLedgerLineId: line, signedAmount: '-20' }] });
    const open = scheduleBlockers(form);
    expect(open.some((b) => b.startsWith('Cash-flow source lineage: movement 2 is not linked'))).toBe(true);
    expect(open.some((b) => b.startsWith('Cash-flow source lineage: movement 3 has linked lines that do not total'))).toBe(true);
    expect(open.some((b) => b.includes('movement 1'))).toBe(false);
    expect(open).toHaveLength(2);
  });

  it('lists a missing disclosure as a blocker', () => {
    const form = fullForm();
    form.disclosures = [];
    expect(scheduleBlockers(form)).toEqual([
      'Disclosures: none entered. Add at least one disclosure response, or a rationale when not applicable.',
    ]);
  });

  it('lists nothing when every schedule is supplied and every movement is linked', () => {
    expect(scheduleBlockers(fullForm())).toEqual([]);
  });

  it('labels the submit control as a draft while any blocker is open', () => {
    expect(queueButtonLabel(scheduleBlockers(completeForm()))).toBe('Queue draft build with open blockers');
    expect(queueButtonLabel(scheduleBlockers(fullForm()))).toBe('Queue package build');
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
