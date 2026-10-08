import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { afterEach, describe, expect, it } from 'vitest';
import { SessionService } from '../../core/session';
import { AuditPlan, decodePlan } from './plan';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';
const hash64 = 'b'.repeat(64);

/** A complete audit-plan payload as the API returns it for a Manager with a draft calculation. */
const planPayload = {
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
  findings: [{ id: id1, findingType: 'MISSTATEMENT', status: 'UNRESOLVED', monetaryAmount: '12000.00' }],
  workpapers: [{ id: id2, index: 'B.01', title: 'Revenue Substantive Testing', status: 'IN_PROGRESS', revision: 1 }],
  materialitySource: {
    mappingVersionId: id3,
    mappingVersion: 1,
    datasetId: id1,
    datasetDigest: hash64,
    currency: 'QAR',
    options: [{ kind: 'REVENUE', destinationCode: 'REV', label: 'Total Revenue', amount: '10000000.00', lineCount: 45 }],
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
    rounding: null,
  },
  rateRanges: [{ kind: 'REVENUE', minRatePercent: '0.50', maxRatePercent: '2.00' }],
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
      assessedByUserId: id2,
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
  fsliStratification: [],
  milestonePlan: {
    id: id2,
    engagementId: id1,
    periodEnd: '2026-12-31',
    statutoryFilingCutoff: '2027-04-30',
    fieldworkStartDate: '2027-01-07',
    draftReportDate: '2027-03-13',
    finalReportDate: '2027-04-15',
    archiveDeadlineDate: '2027-06-14',
    adjustmentReason: null,
    warningOverrideReason: null,
    warnings: [],
    revision: 1,
    scheduledByUserId: id3,
    scheduledAt: '2026-10-06T12:00:00Z',
    canConfigure: true,
  },
  canApplyPracticalRounding: false,
};

/** The effective draft after a practical rounding of PM 100,000.00 to 99,000.00 (STE 3.2). */
const roundedPayload = {
  ...planPayload,
  canApplyPracticalRounding: true,
  latestCalculation: {
    ...planPayload.latestCalculation,
    rounding: {
      decidedAt: '2026-10-06T12:30:00Z',
      decidedByUserId: id3,
      rationale: 'Rounded to the nearest thousand for presentation.',
      adjustedPlanningMateriality: '99000.00',
      adjustedTolerableError: '74000.00',
      adjustedSadThreshold: '4950.00',
      planningDeltaPercent: '-1.00',
      tolerableDeltaPercent: '-1.33',
      sadDeltaPercent: '-1.00',
    },
  },
};

describe('Audit Plan Contracts', () => {
  it('decodes a complete audit plan payload', () => {
    const decoded = decodePlan(planPayload, 'plan');
    expect(decoded.engagementId).toBe(id1);
    expect(decoded.professionalWorkBlocked).toBe(false);
    expect(decoded.materiality?.benchmarkSource).toBe('Revenue');
    expect(decoded.risks.length).toBe(1);
    expect(decoded.populations.length).toBe(1);
    expect(decoded.populations[0].rowCount).toBe(1500);
    expect(decoded.findings[0].monetaryAmount).toBe('12000.00');
    expect(decoded.workpapers[0].index).toBe('B.01');
    expect(decoded.latestCalculation?.planningMateriality).toBe('100000.00');
    expect(decoded.latestCalculation?.rounding).toBeNull();
    expect(decoded.routing[0].fraudRisk).toBe(true);
    expect(decoded.routing[0].assessedByUserId).toBe(id2);
    expect(decoded.team[0].name).toBe('Audit Senior');
    expect(decoded.isPartner).toBe(true);
    expect(decoded.canApplyPracticalRounding).toBe(false);
    expect(decoded.milestonePlan?.statutoryFilingCutoff).toBe('2027-04-30');
    expect(decoded.milestonePlan?.archiveDeadlineDate).toBe('2027-06-14');
  });

  it('rejects invalid plan payloads', () => {
    expect(() => decodePlan(null, 'plan')).toThrow();
    expect(() => decodePlan({}, 'plan')).toThrow();
    expect(() => decodePlan({ engagementId: 'not-a-uuid' }, 'plan')).toThrow();
  });

  it('requires the server rounding flag and keeps rounded figures as exact decimals', () => {
    const withoutFlag: Record<string, unknown> = { ...planPayload };
    delete withoutFlag['canApplyPracticalRounding'];
    expect(() => decodePlan(withoutFlag, 'plan')).toThrow();
    expect(() => decodePlan({ ...planPayload, canApplyPracticalRounding: 'yes' }, 'plan')).toThrow();

    const rounded = decodePlan(roundedPayload, 'plan');
    expect(rounded.canApplyPracticalRounding).toBe(true);
    expect(rounded.latestCalculation?.rounding?.adjustedPlanningMateriality).toBe('99000.00');
    expect(rounded.latestCalculation?.rounding?.planningDeltaPercent).toBe('-1.00');

    const numericRounding = {
      ...roundedPayload,
      latestCalculation: {
        ...roundedPayload.latestCalculation,
        rounding: { ...roundedPayload.latestCalculation.rounding, adjustedPlanningMateriality: 99000 },
      },
    };
    expect(() => decodePlan(numericRounding, 'plan')).toThrow();
  });
});

describe('Practical rounding form (STE 3.2)', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify({ ignoreCancelled: true });
    TestBed.resetTestingModule();
  });

  /** Renders the audit plan for a staff session and answers its single plan request with the given payload. */
  function render(payload: object) {
    const params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    TestBed.configureTestingModule({
      imports: [AuditPlan],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
      ],
    });
    TestBed.inject(SessionService).current.set({ userId: id3, firmId: id2, generation: '1', staff: true });
    const http = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(AuditPlan);
    f.detectChanges();
    http.expectOne(`/api/ui/engagements/${id1}/audit-plan`).flush(payload);
    f.detectChanges();
    return f;
  }

  it('shows the rounding form to a Manager, prefilled with the computed thresholds', async () => {
    const f = render({ ...planPayload, canApplyPracticalRounding: true });
    await f.whenStable();
    f.detectChanges();
    const host = f.nativeElement as HTMLElement;
    const pm = host.querySelector<HTMLInputElement>('input[name="roundPm"]');
    expect(pm).not.toBeNull();
    expect(pm?.value).toBe('100000.00');
    expect(host.querySelector<HTMLInputElement>('input[name="roundTe"]')?.value).toBe('75000.00');
    expect(host.querySelector<HTMLInputElement>('input[name="roundSad"]')?.value).toBe('5000.00');
    expect(host.querySelector('textarea[name="roundRationale"]')).not.toBeNull();
  });

  it('hides the rounding form when the server does not grant rounding', () => {
    const f = render({
      ...planPayload,
      latestCalculation: { ...planPayload.latestCalculation, state: 'APPROVED' },
    });
    expect((f.nativeElement as HTMLElement).querySelector('input[name="roundPm"]')).toBeNull();
  });

  it('shows the computed, effective and delta figures once a rounding is recorded', () => {
    const f = render(roundedPayload);
    const text = (f.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Effective (rounded)');
    expect(text).toContain('Rounded to the nearest thousand for presentation.');
    expect(text).toContain('-1.00%');
    expect(text).toContain('-1.33%');
    expect(text).toContain('Each effective threshold stays within ±5% of its computed value.');
  });
});
