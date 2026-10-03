import { describe, expect, it } from 'vitest';
import { decodePlan } from './plan';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';
const hash64 = 'b'.repeat(64);

describe('Audit Plan Contracts', () => {
  it('decodes a complete audit plan payload', () => {
    const raw = {
      engagementId: id1,
      professionalWorkBlocked: false,
      materiality: {
        id: id2,
        preparedByUserId: id3,
        benchmarkSource: 'Revenue',
        benchmarkVersion: '1.0',
        rationale: 'Standard commercial entity benchmark',
        benchmarkAmount: '10000000.00',
        rateApplied: '0.0100',
        overallMateriality: '100000.00',
        performanceMateriality: '75000.00',
        clearlyTrivialThreshold: '5000.00',
        qualitativeConsiderations: 'First year audit consideration',
        status: 'SUBMITTED',
        approvedByUserId: null,
        approvedAt: null,
      },
      canApproveMateriality: true,
      risks: [
        {
          id: id3,
          accountArea: 'Revenue',
          description: 'Risk of premature revenue recognition',
          assertion: 'Cutoff',
          severity: 'HIGH',
          status: 'ASSESSED',
        },
      ],
      populations: [
        {
          id: id2,
          purpose: 'Sales invoice testing',
          assertion: 'Occurrence',
          rowCount: 1500,
          monetaryControlTotal: '10500000.00',
          currency: 'QAR',
          status: 'EXTRACTED',
        },
      ],
      findings: [
        {
          id: id1,
          findingType: 'MISSTATEMENT',
          status: 'UNRESOLVED',
          monetaryAmount: '12000.00',
        },
      ],
      workpapers: [
        {
          id: id2,
          index: 'B.01',
          title: 'Revenue Substantive Testing',
          status: 'IN_PROGRESS',
          revision: 1,
        },
      ],
      materialitySource: {
        mappingVersionId: id3,
        mappingVersion: 1,
        datasetId: id1,
        datasetDigest: hash64,
        currency: 'QAR',
        options: [
          {
            kind: 'REVENUE',
            destinationCode: 'REV',
            label: 'Total Revenue',
            amount: '10000000.00',
            lineCount: 45,
          },
        ],
      },
      materialitySourceMessage: null,
      latestCalculation: {
        assessmentId: id2,
        state: 'DRAFT',
        route: 'NORMAL',
        benchmarkKind: 'REVENUE',
        destinationCode: 'REV',
        benchmarkAmount: '10000000.00',
        currency: 'QAR',
        sourceLineCount: 45,
        mappingVersionNumber: 1,
        ratePercent: '1.00',
        performancePercent: '75.00',
        trivialPercent: '5.00',
        planningMateriality: '100000.00',
        tolerableError: '75000.00',
        sadThreshold: '5000.00',
        policyVersion: '2026.1',
      },
      rateRanges: [
        {
          kind: 'REVENUE',
          minRatePercent: '0.50',
          maxRatePercent: '2.00',
        },
      ],
      performanceMin: '50.00',
      performanceMax: '85.00',
      trivialMin: '3.00',
      trivialMax: '5.00',
      riskRuleVersion: 'ISA-315-R2',
      routing: [
        {
          riskId: id3,
          area: 'Revenue',
          assertion: 'Cutoff',
          significanceDecision: 'SIGNIFICANT',
          assessmentId: id2,
          band: 'HIGH',
          likelihood: 4,
          magnitude: 4,
          fraudRisk: true,
          route: 'PARTNER_REVIEW',
          partnerReviewRequired: true,
          partnerCleared: false,
          partnerName: null,
          ownerName: 'Senior Auditor',
          ownerLevel: 'SENIOR',
        },
      ],
      team: [
        {
          assignmentId: id1,
          userId: id3,
          name: 'Audit Senior',
          level: 'SENIOR',
          levelLabel: 'Senior Auditor',
          authorizationRole: 'AuditStaff',
          certified: true,
        },
      ],
      canAssignOwners: true,
      isPartner: true,
    };

    const decoded = decodePlan(raw, 'plan');
    expect(decoded.engagementId).toBe(id1);
    expect(decoded.professionalWorkBlocked).toBe(false);
    expect(decoded.materiality?.benchmarkSource).toBe('Revenue');
    expect(decoded.risks.length).toBe(1);
    expect(decoded.populations.length).toBe(1);
    expect(decoded.populations[0].rowCount).toBe(1500);
    expect(decoded.findings[0].monetaryAmount).toBe('12000.00');
    expect(decoded.workpapers[0].index).toBe('B.01');
    expect(decoded.latestCalculation?.planningMateriality).toBe('100000.00');
    expect(decoded.routing[0].fraudRisk).toBe(true);
    expect(decoded.team[0].name).toBe('Audit Senior');
    expect(decoded.isPartner).toBe(true);
  });

  it('rejects invalid plan payloads', () => {
    expect(() => decodePlan(null, 'plan')).toThrow();
    expect(() => decodePlan({}, 'plan')).toThrow();
    expect(() => decodePlan({ engagementId: 'not-a-uuid' }, 'plan')).toThrow();
  });
});
