import { describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { Api } from '../../core/api';
import { decodeCashFlowSourceReview, decodePackage, latestCompositionDecisionId, packageReviewIntentFor, packageReviewRequestFor,
  readPendingSourceIntent, sourceIntentFor, sourceReviewRequestFor, writePendingSourceIntent } from './package';
import { FinancialPackage } from './package';
import { decodeReviewQueue } from './reviews';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';
const hash64 = 'a'.repeat(64);

describe('Financial Package and Review Queue Contracts', () => {
  it('decodes a valid financial package payload', () => {
    const raw = {
      id: id1,
      engagementId: id2,
      comparativePackageId: null,
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
      cashAccountCodes: ['1000'],
      openingCashEvidenceReferences: ['opening-cash-evidence'],
      closingCashEvidenceReferences: ['closing-cash-evidence'],
      validations: [
        { code: 'BS_BALANCED', passed: true, detail: 'Balance sheet balances exactly.' },
      ],
      statementTotals: [
        { section: 'ASSETS', amount: '500000.00' },
      ],
      cashFlow: [
        { id: id2, section: 'OPERATING', description: 'Operating cash flow', amount: '50000.00', isNonCash: false, currency: 'QAR',
          movementDate: '2026-06-30', activityCode: 'OPERATING_RESULT', sourceReferences: ['gl-batch-1'], classificationRationale: 'Agreed from the accepted general ledger.' },
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
          presentationRevisionId: id1,
          compositionId: id2,
          compositionManifestDigest: hash64,
          id: id1,
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
    expect(decoded.reviews[0].compositionId).toBe(id2);
    expect(decoded.reviews[0].compositionManifestDigest).toBe(hash64);
    expect(decoded.artifact?.byteCount).toBe(2048);
    expect(decoded.canReview).toBe(true);
  });

  it('builds a formal review request bound to the exact selected composition', () => {
    const composition = { revisionId: id1, compositionId: id2, manifestDigest: hash64 };
    expect(packageReviewRequestFor({ stage: 'PARTNER_APPROVAL', decision: 'APPROVED', mode: 'SIGNED_IN', reference: 'partner-review-1', comment: '' }, composition))
      .toEqual({ stage: 'PARTNER_APPROVAL', decision: 'APPROVED', evidenceMode: 'SIGNED_IN', evidenceReference: 'partner-review-1', comment: null,
        presentationRevisionId: id1, presentationCompositionId: id2, compositionManifestDigest: hash64 });
    expect(packageReviewRequestFor({ stage: 'PARTNER_APPROVAL', decision: 'APPROVED', mode: 'SIGNED_IN', reference: 'partner-review-1', comment: '' }, null)).toBeNull();
  });

  it('sends the original intent and the latest decision on the selected composition chain (F-04)', () => {
    const composition = { revisionId: id1, compositionId: id2, manifestDigest: hash64 };
    const other = { id: '99999999-9999-4999-8999-999999999999', presentationRevisionId: id1, compositionId: id1, compositionManifestDigest: hash64 };
    const first = { id: '77777777-7777-4777-8777-777777777777', presentationRevisionId: id1, compositionId: id2, compositionManifestDigest: hash64 };
    const second = { id: '88888888-8888-4888-8888-888888888888', presentationRevisionId: id1, compositionId: id2, compositionManifestDigest: hash64 };
    expect(latestCompositionDecisionId([], composition)).toBeNull();
    expect(latestCompositionDecisionId([first, second, other], composition)).toBe(second.id);
    const form = { stage: 'ACCOUNTING_REVIEW', decision: 'APPROVED', mode: 'SIGNED_IN', reference: 'review-1', comment: '' };
    const intent = packageReviewIntentFor(null, form, composition, second.id, 'request-1');
    expect(packageReviewIntentFor(intent, form, composition, second.id, 'request-2')).toBe(intent);
    expect(packageReviewIntentFor(intent, { ...form, decision: 'CHANGES_REQUIRED' }, composition, second.id, 'request-2').requestId).toBe('request-2');
    expect(packageReviewIntentFor(intent, form, composition, null, 'request-3').requestId).toBe('request-3');
    expect(packageReviewRequestFor(form, composition, { requestId: intent.requestId, expectedLatestDecisionId: second.id }))
      .toMatchObject({ requestId: 'request-1', expectedLatestDecisionId: second.id, presentationCompositionId: id2 });
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

  it('rejects a malformed review composition digest', () => {
    const invalidReview = { stage: 'ACCOUNTING_REVIEW', decision: 'APPROVED', evidenceMode: 'SIGNED_IN', evidenceReference: 'review',
      packageHash: hash64, decidedAt: '2026-10-02T15:00:00Z', presentationRevisionId: id1, compositionId: id2, compositionManifestDigest: 'bad', id: id1 };
    const validBase = {
      id: id1, engagementId: id2, comparativePackageId: null, status: 'VALIDATED', framework: 'IFRS', periodStart: '2026-01-01', periodEnd: '2026-12-31',
      currency: 'QAR', templateVersion: '1.0', calculationHash: hash64, supplementaryHash: null, revision: 1, cashBeginning: null, cashEnding: null,
      cashAccountCodes: [], openingCashEvidenceReferences: [], closingCashEvidenceReferences: [], validations: [], statementTotals: [], cashFlow: [], disclosures: [],
      reviews: [invalidReview], artifact: null, canReview: true, reviewStages: [], reviewDecisions: [],
    };
    expect(() => decodePackage(validBase, 'package')).toThrow();
  });
});

describe('Cash-flow source review contract (CP-11)', () => {
  it('replays a saved decision with its original digest and predecessor after reload', () => {
    const originalDigest = 'a'.repeat(64);
    const changedDigest = 'b'.repeat(64);
    const first = sourceIntentFor(null, 'APPROVED', null, originalDigest, id1, id2);
    expect(writePendingSourceIntent(id1, first)).toBe(true);
    const replay = sourceIntentFor(readPendingSourceIntent(id1), 'APPROVED', null, changedDigest, null, id1);
    writePendingSourceIntent(id1, null);

    expect(replay.requestId).toBe(id2);
    expect(sourceReviewRequestFor(replay)).toEqual({ decision: 'APPROVED', reason: null, requestId: id2,
      expectedLinkDigest: originalDigest, expectedPreviousReviewId: id1 });
  });

  it('refuses to claim a pending intent is durable when session storage fails', () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('storage blocked'); });
    try {
      expect(writePendingSourceIntent(id1, sourceIntentFor(null, 'APPROVED', null, 'a'.repeat(64), null, id2))).toBe(false);
    } finally {
      setItem.mockRestore();
    }
  });

  it('starts a new intent when the reviewer changes the decision or reason', () => {
    const first = sourceIntentFor(null, 'APPROVED', null, 'a'.repeat(64), null, id1);
    const changedDecision = sourceIntentFor(first, 'RETURNED', null, 'b'.repeat(64), id2, id2);
    const changedReason = sourceIntentFor(first, 'APPROVED', 'Re-check support.', 'b'.repeat(64), id2, id3);

    expect(changedDecision.requestId).toBe(id2);
    expect(changedDecision.expectedLinkDigest).toBe('b'.repeat(64));
    expect(changedDecision.expectedPreviousReviewId).toBe(id2);
    expect(changedReason.requestId).toBe(id3);
    expect(changedReason.reason).toBe('Re-check support.');
  });

  const review = {
    financialPackageId: id1, movements: 2, resolvedMovements: 2, sourceLinks: 2, currentSourceImportBatchId: id2, approved: true, canReview: false,
    fxAssessmentState: 'ASSESSED_ZERO', fxAssessmentEvidenceReference: 'functional-currency-cash',
    fxAssessmentRationale: 'No foreign-currency cash effect.', fxAssessmentMatchesEffects: true,
    reviews: [{ id: id1, decision: 'APPROVED', reason: '', reviewedByUserId: id2, reviewedAt: '2026-10-14T10:00:00Z', sourceImportBatchId: id2 }],
    linkDigest: 'a'.repeat(64), latestReviewId: id1,
  };

  it('decodes the approval state and its decision history', () => {
    const decoded = decodeCashFlowSourceReview(review, 'review');
    expect(decoded.approved).toBe(true);
    expect(decoded.resolvedMovements).toBe(decoded.movements);
    expect(decoded.reviews[0].decision).toBe('APPROVED');
  });

  it('refuses an unknown decision and a malformed reviewer identity', () => {
    expect(() => decodeCashFlowSourceReview({ ...review, reviews: [{ ...review.reviews[0], decision: 'PENDING' }] }, 'review')).toThrow();
    expect(() => decodeCashFlowSourceReview({ ...review, reviews: [{ ...review.reviews[0], reviewedByUserId: 'me' }] }, 'review')).toThrow();
  });
});

describe('Cash-flow source review recovery UI (CP-11)', () => {
  const reviewState = {
    financialPackageId: id1, movements: 1, resolvedMovements: 1, sourceLinks: 1, currentSourceImportBatchId: id2,
    approved: false, canReview: true, reviews: [], linkDigest: hash64, latestReviewId: null,
    fxAssessmentState: 'ASSESSED_ZERO', fxAssessmentEvidenceReference: 'functional-currency-cash',
    fxAssessmentRationale: 'No foreign-currency cash effect.', fxAssessmentMatchesEffects: true,
  };
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let command: ReturnType<typeof vi.fn>;
  let fixture: ReturnType<typeof TestBed.createComponent<FinancialPackage>>;

  beforeEach(() => {
    sessionStorage.clear();
    params = new BehaviorSubject(convertToParamMap({ id: id1 }));
    const resource = { data: () => reviewState, error: () => '', loading: () => false, reload: vi.fn() };
    command = vi.fn(async () => ({ ok: false as const, unknown: false as const, code: 'revision.stale', message: 'Review is stale.', status: 409 }));
    TestBed.configureTestingModule({ imports: [FinancialPackage], providers: [
      { provide: ActivatedRoute, useValue: { paramMap: params } },
      { provide: Api, useValue: { resource: () => resource, command } },
    ] });
    TestBed.overrideComponent(FinancialPackage, { set: { template: '<button [disabled]="sourceIntentRecoveryBlocked()" (click)="reviewSources(id()!)">Review</button>' } });
    fixture = TestBed.createComponent(FinancialPackage);
  });

  afterEach(() => { vi.restoreAllMocks(); TestBed.resetTestingModule(); sessionStorage.clear(); });

  it('does not dispatch when recovery storage is unavailable', async () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('storage disabled'); });
    fixture.componentInstance.reviewSources(id1);
    await fixture.whenStable();

    expect(command).not.toHaveBeenCalled();
    expect(fixture.componentInstance.cmd.message()).toContain('no decision was sent');
  });

  it('blocks another decision when a known-stale intent cannot be removed', async () => {
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => { throw new Error('storage unavailable'); });
    fixture.componentInstance.reviewSources(id1);
    await fixture.whenStable();
    await Promise.resolve();
    await Promise.resolve();

    expect(command).toHaveBeenCalledTimes(1);
    expect(fixture.componentInstance.sourceIntentRecoveryBlocked()).toBe(true);
    expect(fixture.nativeElement.querySelector('button').disabled).toBe(true);
    expect(fixture.componentInstance.cmd.message()).toContain('no new decision was sent');

    fixture.componentInstance.reviewSources(id1);
    await fixture.whenStable();
    expect(command).toHaveBeenCalledTimes(1);
  });
});
