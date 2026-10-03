import { describe, expect, it } from 'vitest';
import { decodePackage } from './package';
import { decodeReviewQueue } from './reviews';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const hash64 = 'a'.repeat(64);

describe('Financial Package and Review Queue Contracts', () => {
  it('decodes a valid financial package payload', () => {
    const raw = {
      id: id1,
      engagementId: id2,
      status: 'VALIDATED',
      framework: 'IFRS',
      periodStart: '2026-01-01',
      periodEnd: '2026-12-31',
      currency: 'QAR',
      templateVersion: '1.0',
      calculationHash: hash64,
      supplementaryHash: null,
      revision: 1,
      cashBeginning: '100000.00',
      cashEnding: '150000.00',
      validations: [
        { code: 'BS_BALANCED', passed: true, detail: 'Balance sheet balances exactly.' },
      ],
      statementTotals: [
        { section: 'ASSETS', amount: '500000.00' },
      ],
      cashFlow: [
        { section: 'OPERATING', description: 'Operating cash flow', amount: '50000.00' },
      ],
      disclosures: [
        { code: 'NOTE_01', response: 'Summary of significant accounting policies.', notApplicable: false, rationale: null },
      ],
      reviews: [
        {
          stage: 'ACCOUNTING_REVIEW',
          decision: 'APPROVED',
          evidenceMode: 'SIGNED_IN',
          evidenceReference: 'Internal working paper review',
          packageHash: hash64,
          decidedAt: '2026-10-02T15:00:00Z',
        },
      ],
      artifact: {
        artifactSha256Hex: hash64,
        byteCount: 2048,
        renderedText: 'Financial Package Report Content',
      },
      canReview: true,
      reviewStages: ['ACCOUNTING_REVIEW', 'PARTNER_APPROVAL'],
      reviewDecisions: ['APPROVED', 'RETURNED'],
    };

    const decoded = decodePackage(raw, 'package');
    expect(decoded.id).toBe(id1);
    expect(decoded.engagementId).toBe(id2);
    expect(decoded.status).toBe('VALIDATED');
    expect(decoded.validations.length).toBe(1);
    expect(decoded.validations[0].passed).toBe(true);
    expect(decoded.statementTotals[0].amount).toBe('500000.00');
    expect(decoded.reviews[0].stage).toBe('ACCOUNTING_REVIEW');
    expect(decoded.artifact?.byteCount).toBe(2048);
    expect(decoded.canReview).toBe(true);
  });

  it('decodes a valid review queue payload', () => {
    const raw = [
      {
        packageId: id1,
        framework: 'IFRS',
        periodStart: '2026-01-01',
        periodEnd: '2026-12-31',
        currency: 'QAR',
        packageHash: hash64,
        managementDecision: 'APPROVED',
        accountingDecision: 'APPROVED',
        partnerDecision: 'PENDING',
        nextAction: 'PARTNER_APPROVAL_REQUIRED',
      },
    ];

    const decoded = decodeReviewQueue(raw, 'reviewQueue');
    expect(decoded.length).toBe(1);
    expect(decoded[0].packageId).toBe(id1);
    expect(decoded[0].nextAction).toBe('PARTNER_APPROVAL_REQUIRED');
  });

  it('rejects invalid package payloads', () => {
    expect(() => decodePackage(null, 'package')).toThrow();
    expect(() => decodePackage({}, 'package')).toThrow();
    expect(() => decodePackage({ id: 'bad-guid' }, 'package')).toThrow();
  });
});
