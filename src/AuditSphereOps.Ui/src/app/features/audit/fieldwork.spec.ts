import { describe, expect, it } from 'vitest';
import { decodeFieldwork, decodeProcedureReview, decodeSampleSet } from './fieldwork';

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
          applicabilityStatus: 'APPLICABLE',
          status: 'COMPLETED',
        },
      ],
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
});
