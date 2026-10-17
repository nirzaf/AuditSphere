import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { BehaviorSubject, Subject } from 'rxjs';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { afterEach, describe, expect, it } from 'vitest';
import { AuditFieldwork, decodeFieldwork, decodeProcedureReview, decodeProgramOptions, decodeSampleSet, focusRisks } from './fieldwork';

const id1 = '11111111-1111-4111-8111-111111111111';
const id2 = '22222222-2222-4222-8222-222222222222';
const id3 = '33333333-3333-4333-8333-333333333333';
const hash64 = 'd'.repeat(64);

describe('Audit Fieldwork Contracts', () => {
  it('decodes a valid fieldwork coverage payload', () => {
    const raw = {
      engagementId: id1,
      catalogProcedureCount: 150,
      catalogVersion: '2026.1',
      program: {
        programCode: 'CORE-AUDIT',
        version: '2026.1',
        sourceHash: hash64,
      },
      procedures: [
        {
          id: id2,
          sourceProcedureId: 'AWP-01',
          sourceSectionNumber: 1,
          sourceSectionTitle: 'Planning',
          title: 'Preliminary Analytical Review',
          originalWording: 'Review the controlled source procedure.',
          effectiveWording: 'Review the engagement-specific effective instructions.',
          definitionRevision: 1,
          definitionProvenance: 'AUTHORED_REVISION',
          applicabilityStatus: 'APPLICABLE',
          status: 'COMPLETED',
          riskId: null,
        },
      ],
      risks: [{ id: id3, area: 'Cash', significanceDecision: 'NORMAL', band: 'GREEN', effectiveBand: 'AMBER',
        balance: '15000.00', currency: 'USD', tolerableError: '15000.00', planningMateriality: '20000.00',
        riskAssessmentId: id1, materialityAssessmentId: id2, materialityCalculationId: id2, mappingVersionId: id1,
        mappingVersionNumber: 1, datasetId: id2, datasetDigest: 'a'.repeat(64), destinationCode: 'CASH',
        statementSection: 'ASSETS', ruleVersion: 'STE-RISK-BAND-2026.1+STE-FSLI-RISK-2026.1',
        explanation: 'Absolute balance is at tolerable error.', blocker: null }],
      differences: [
        {
          currency: 'QAR',
          differenceCount: 2,
          grossAmount: '25000.00',
          signedNetAmount: '15000.00',
          unadjustedGrossAmount: '10000.00',
          unadjustedSignedNetAmount: '5000.00',
          correctedGrossAmount: '15000.00',
          correctedSignedNetAmount: '10000.00',
        },
      ],
      aggregate: {
        id: id3,
        status: 'SUBMITTED',
        conclusion: 'Differences are within acceptable tolerable error.',
        preparedByMe: true,
      },
      aggregateCurrentAndReviewed: true,
      schedules: [
        {
          id: id1,
          scheduleType: 'ACCOUNTS_RECEIVABLE',
          rowCount: 450,
          currency: 'QAR',
        },
      ],
      samplingMethods: ['MUS', 'SYSTEMATIC', 'KEY_ITEM', 'RANDOM'],
      samplingRuns: [
        {
          id: id2,
          selectionId: id3,
          selectionStatus: 'REVIEWED',
          preparedByMe: false,
          canReviewSelection: false,
          createdAt: '2026-10-01T14:30:00Z',
          method: 'MUS',
          interval: '50000.00',
          keyItemThreshold: '100000.00',
          sampleSize: 35,
          seed: 42,
          selectedCount: 35,
          populationCount: 450,
          coveragePercent: '82.50',
          sourceDigest: hash64,
          selectionDigest: hash64,
          engineVersion: 'audit-sampling-engine.v1',
          previewDigest: hash64,
          orderingPolicy: 'SCHEDULE_SOURCE_LINE_THEN_STABLE_ROW_ID',
          attributeFields: [],
          reproduces: true,
        },
      ],
      evidenceCandidates: [
        {
          uploadIntentId: id3,
          pbcRequestId: id1,
          requestArea: 'Cash',
          fileName: 'bank_statement_dec2026.pdf',
          contentSha256: hash64,
          receivedAt: '2026-10-01T16:00:00Z',
          superseded: false,
        },
      ],
      physicalItems: [
        {
          id: id2,
          fileIndex: 'PF-01',
          boxReference: 'BOX-2026-A',
          description: 'Original title deeds and land registry certificates',
          currentLocation: 'Firm Safe Room A',
          movements: [
            {
              toLocation: 'Firm Safe Room A',
              movedAt: '2026-09-20T11:00:00Z',
            },
          ],
          procedureTitles: ['Land and Buildings Title Verification'],
        },
      ],
      canManageFieldwork: true,
      canReviewSelections: true,
      canViewReviewNotes: true,
      canAddOrResolveReviewNotes: true,
      canRespondToReviewNotes: true,
    };

    const decoded = decodeFieldwork(raw, 'fieldwork');
    expect(decoded.engagementId).toBe(id1);
    expect(decoded.program?.programCode).toBe('CORE-AUDIT');
    expect(decoded.procedures.length).toBe(1);
    expect(decoded.procedures[0].sourceProcedureId).toBe('AWP-01');
    expect(decoded.procedures[0].originalWording).toContain('controlled source');
    expect(decoded.procedures[0].effectiveWording).toContain('engagement-specific');
    expect(decoded.procedures[0].definitionProvenance).toBe('AUTHORED_REVISION');
    expect(decoded.risks[0].effectiveBand).toBe('AMBER');
    expect(decoded.risks[0].balance).toBe('15000.00');
    expect(decoded.risks[0].materialityCalculationId).toBe(id2);
    expect(decoded.risks[0].datasetDigest).toBe('a'.repeat(64));
    expect(decoded.differences.length).toBe(1);
    expect(decoded.aggregate?.conclusion).toContain('tolerable error');
    expect(decoded.samplingRuns[0].method).toBe('MUS');
    expect(decoded.samplingRuns[0].selectionStatus).toBe('REVIEWED');
    expect(decoded.physicalItems[0].fileIndex).toBe('PF-01');
    expect(decoded.canManageFieldwork).toBe(true);
    expect(decoded.canReviewSelections).toBe(true);
    expect(decoded.canViewReviewNotes).toBe(true);
    expect(decoded.canAddOrResolveReviewNotes).toBe(true);
    expect(decoded.canRespondToReviewNotes).toBe(true);
  });

  it('decodes the selected sample with execution evidence and independent review state', () => {
    const decoded = decodeSampleSet({
      selectionId: id1, procedureId: id2, method: 'ATTRIBUTE_STRATA', rationale: 'Cover the approved account groups.', status: 'REVIEWED',
      selectedCount: 1, selectedSignedTotal: '-100.00', inputGeneration: 2, testedCount: 1, exceptionCount: 1,
      cutOffRecordedCount: 0, subsequentMatchedCount: 0, totalCount: 1, page: 1, pageSize: 100,
      items: [{ selectionItemId: id3, stableRowId: 'ROW-009', signedAmount: '-100.00', currency: 'QAR', inclusionReason: 'Account group: 4000',
        testResult: 'EXCEPTION', testRevision: 1, exceptionAmount: '100.00', auditItemTestId: id1,
        testWorkPerformed: 'Agreed to the invoice and dispatch record.', evidenceReferences: ['invoice-9', 'dispatch-9'],
        contradictoryEvidence: 'Dispatch occurred after year end.', followUp: 'Assess the cut-off adjustment.', testReviewed: false,
        testReviewDecision: 'CHANGES_REQUIRED', testReviewComment: 'Obtain the signed delivery note.', canReviewTest: true }],
    }, 'sampleSet');

    expect(decoded.items[0].stableRowId).toBe('ROW-009');
    expect(decoded.items[0].evidenceReferences).toEqual(['invoice-9', 'dispatch-9']);
    expect(decoded.items[0].followUp).toContain('cut-off');
    expect(decoded.items[0].canReviewTest).toBe(true);
  });

  it('decodes a valid procedure review payload', () => {
    const raw = {
      procedureId: id1,
      currentResult: {
        id: id2,
        revision: 1,
        workPerformed: 'Tested sample of 35 sales transactions to dispatch notes and invoices.',
        conclusion: 'Satisfactory without exception.',
      },
      notes: [
        {
          noteId: id3,
          resultRevision: 1,
          field: 'workPerformed',
          excerpt: 'dispatch notes',
          startOffset: 32,
          body: 'Ensure sequential numbering was checked.',
          authorName: 'Audit Manager',
          createdAt: '2026-10-02T10:00:00Z',
          open: true,
          events: [
            {
              id: id1,
              kind: 'REPLY',
              body: 'Checked sequentially, no missing serial numbers identified.',
              createdAt: '2026-10-02T11:00:00Z',
            },
          ],
        },
      ],
      evidence: [
        {
          linkId: id2,
          uploadIntentId: id3,
          fileName: 'sales_listing.xlsx',
          contentSha256: hash64,
          note: 'Reconciles to general ledger',
          linkedAt: '2026-10-02T09:00:00Z',
        },
      ],
    };

    const decoded = decodeProcedureReview(raw, 'procedureReview');
    expect(decoded.procedureId).toBe(id1);
    expect(decoded.currentResult?.conclusion).toBe('Satisfactory without exception.');
    expect(decoded.notes.length).toBe(1);
    expect(decoded.notes[0].open).toBe(true);
    expect(decoded.notes[0].events.length).toBe(1);
    expect(decoded.evidence[0].fileName).toBe('sales_listing.xlsx');
  });

  it('rejects invalid fieldwork payloads', () => {
    expect(() => decodeFieldwork(null, 'fieldwork')).toThrow();
    expect(() => decodeFieldwork({}, 'fieldwork')).toThrow();
    expect(() => decodeProcedureReview(null, 'procedureReview')).toThrow();
  });

  it('decodes engagement-scoped programme options and separate baseline/ad-hoc counts', () => {
    const decoded = decodeProgramOptions({ engagementId: id1, currentAdoptionId: null, currentProgramVersionId: null,
      currentVersion: null, currentSourceHash: null, currentContentDigest: null, baselineProcedureCount: 0,
      adHocProcedureCount: 0, currentAdoptionComplete: false, currentAdoptionBlocker: null,
      options: [{ programVersionId: id2, programCode: 'CORE-AUDIT', version: '2026.1', sourceHash: hash64,
        contentDigest: hash64, procedureCount: 165, eligible: true, blocker: null }] }, 'programOptions');
    expect(decoded.options[0].eligible).toBe(true);
    expect(decoded.options[0].procedureCount).toBe(165);
    expect(() => decodeProgramOptions({ engagementId: id1, options: [] }, 'programOptions')).toThrow();
  });
});

describe('statement focus (STE-NXT-013)', () => {
  it('focuses only the risks recorded against the statement line', () => {
    const risks = [
      { destinationCode: 'REV-100', area: 'Revenue' },
      { destinationCode: 'AR-200', area: 'Receivables' },
      { destinationCode: null, area: 'Other' },
    ];
    expect(focusRisks(risks, 'REV-100')).toEqual([{ destinationCode: 'REV-100', area: 'Revenue' }]);
    expect(focusRisks(risks, 'MISSING')).toEqual([]);
    expect(focusRisks(risks, null)).toEqual([]);
  });
});


describe('Procedure risk link reviewed preparation', () => {
  afterEach(() => TestBed.resetTestingModule());
  function open() {
    const workspace = signal({ engagementId: id1, canManageFieldwork: true, aggregate: null, risks: [{ id: id3 }], procedures: [{ id: id2, riskId: null as string | null }] });
    const api = { resource: vi.fn(() => ({ data: workspace, loading: signal(false), error: signal(''), reload: vi.fn() })), command: vi.fn(), get: vi.fn() };
    TestBed.configureTestingModule({ providers: [{ provide: Api, useValue: api },
      { provide: SessionService, useValue: { current: signal({ userId: id1, firmId: id1, generation: '1', staff: true }), invalidation: signal(0) } },
      { provide: TabDrafts, useValue: { readPendingRequest: () => ({ state: 'absent' }) } }, { provide: Router, useValue: { events: new Subject() } },
      { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ id: id1 })) } }] });
    const page = TestBed.runInInjectionContext(() => new AuditFieldwork()); TestBed.tick(); return { page, workspace, api };
  }
  it('ignores an older procedure review after a newer selection', async () => {
    const { page, workspace, api } = open();
    workspace.set({ ...workspace(), procedures: [{ id: id2, riskId: null }, { id: id3, riskId: null }] }); TestBed.tick();
    let first!: (value: unknown) => void, second!: (value: unknown) => void;
    api.get.mockImplementationOnce(() => new Promise(resolve => first = resolve))
      .mockImplementationOnce(() => new Promise(resolve => second = resolve));
    const older = page.selectProcedure(id2), newer = page.selectProcedure(id3);
    second({ procedureId: id3 }); await newer;
    first({ procedureId: id2 }); await older;
    expect(page.review()?.procedureId).toBe(id3);
  });
  it('discards review results after scope refresh and rejects mismatched procedure identity', async () => {
    const { page, workspace, api } = open(); let complete!: (value: unknown) => void;
    api.get.mockImplementationOnce(() => new Promise(resolve => complete = resolve));
    const pending = page.selectProcedure(id2);
    workspace.set({ ...workspace() }); TestBed.tick(); complete({ procedureId: id2 }); await pending;
    expect(page.review()).toBeNull();
    api.get.mockResolvedValueOnce({ procedureId: id3 }); await page.selectProcedure(id2);
    expect(page.review()).toBeNull(); expect(page.reviewError()).toBe('Procedure review unavailable.');
  });
  it('does not publish a pending procedure review after destruction', async () => {
    const { page, api } = open(); let complete!: (value: unknown) => void;
    api.get.mockImplementationOnce(() => new Promise(resolve => complete = resolve));
    const pending = page.selectProcedure(id2); TestBed.resetTestingModule();
    complete({ procedureId: id2 }); await pending; expect(page.review()).toBeNull();
  });
  it('previews selected risk and exact prior link, never calls the legacy risk write', async () => {
    const { page, api } = open(), prepare = vi.spyOn(page.riskLink, 'prepare').mockResolvedValue();
    page.setRiskSelection(id2, id3); await page.saveRiskLink(id2);
    expect(prepare.mock.calls[0][0]()).toEqual({ action: 'LINK_PROCEDURE_RISK', linkProcedureRisk: { procedureId: id2, riskId: id3, expectedRiskId: null } });
    expect(api.command).not.toHaveBeenCalled();
    page.riskLink.reviewed.set(true); page.setRiskSelection(id2, ''); expect(page.riskLink.reviewed()).toBe(false);
  });
  it('refuses guessed risks, unknown procedures, busy and read-only commands', async () => {
    const { page, workspace } = open(), prepare = vi.spyOn(page.riskLink, 'prepare').mockResolvedValue();
    page.setRiskSelection(id2, id1); await page.saveRiskLink(id2); await page.saveRiskLink(id1);
    page.setRiskSelection(id2, id3); page.cmd.busy.set(true); await page.saveRiskLink(id2); page.cmd.busy.set(false);
    workspace.set({ ...workspace(), canManageFieldwork: false }); await page.saveRiskLink(id2); expect(prepare).not.toHaveBeenCalled();
  });
  it('clears stale local link selections after the persisted procedure link changes', () => {
    const { page, workspace } = open(); page.setRiskSelection(id2, id3);
    workspace.set({ ...workspace(), procedures: [{ id: id2, riskId: id1 }] }); TestBed.tick();
    expect(page.riskSelection(workspace().procedures[0] as Parameters<AuditFieldwork['riskSelection']>[0])).toBe(id1);
  });
});
