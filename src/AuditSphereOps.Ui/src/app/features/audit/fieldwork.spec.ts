import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { BehaviorSubject, Subject } from 'rxjs';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { TabDrafts } from '../../core/tab-drafts';
import { afterEach, describe, expect, it } from 'vitest';
import { AuditFieldwork } from './fieldwork';
import { FieldworkProcedureState } from './fieldwork-procedure-state';
import { decodeFieldwork, decodeProcedureCommandLookup, decodeProcedureCommandPreview, decodeProcedureCommandReceipt, decodeProcedureCommandState, decodeProcedureReview, decodeProgramOptions, decodeSampleSet, focusRisks } from './fieldwork-contracts';

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
          applicabilityRevision: 0,
          applicabilityDecisionId: null,
          pendingApplicabilityProposalId: null,
          applicabilityRationale: null,
          applicabilityHistory: [],
          canProposeNotApplicable: true,
          canApproveNotApplicable: false,
          canReturnNotApplicable: false,
          evidenceState: 'REVIEWED_CURRENT',
          supportLinks: [{ toolKind: 'WORKSPACE', label: 'Record work', route: null, explanation: 'Manual work remains required.', availability: 'AVAILABLE' }],
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
      physicalEvidence: [],
    };

    const decoded = decodeProcedureReview(raw, 'procedureReview');
    expect(decoded.procedureId).toBe(id1);
    expect(decoded.currentResult?.conclusion).toBe('Satisfactory without exception.');
    expect(decoded.notes.length).toBe(1);
    expect(decoded.notes[0].open).toBe(true);
    expect(decoded.notes[0].events.length).toBe(1);
    expect(decoded.evidence[0].fileName).toBe('sales_listing.xlsx');
  });

  it('decodes exact procedure command state, preview, receipt and lookup', () => {
    const basis = { procedureId: id2, sourceProcedureId: 'AWP-08-01', title: 'Cash confirmation', applicabilityStatus: 'APPLICABLE',
      procedureStatus: 'IN_PROGRESS', definitionRevision: 1, inputGeneration: 2, currentResultRevision: 0,
      latestResultId: null, latestResultRevision: 0, latestResultStatus: null, latestReviewId: null,
      latestReviewDecision: null, canSubmit: true, canReview: false, blockers: [] };
    const state = decodeProcedureCommandState({ basis, stateHash: hash64 }, 'state');
    expect(state.basis.definitionRevision).toBe(1);
    const preview = decodeProcedureCommandPreview({ requestId: id1, requestHash: hash64, action: 'SUBMIT_RESULT',
      basis, canProceed: true, blockers: [] }, 'preview');
    expect(preview.action).toBe('SUBMIT_RESULT');
    const receipt = decodeProcedureCommandReceipt({ id: id3, procedureId: id2, sourceProcedureId: 'AWP-08-01',
      action: 'SUBMIT_RESULT', actorUserId: id1, actorEpoch: 1, requestId: id1, requestHash: hash64,
      definitionRevision: 1, inputGeneration: 2, expectedResultRevision: 0, resultId: id2, workpaperId: id3,
      reviewId: null, resultRevision: 1, resultStatus: 'SUBMITTED', decision: null, evidenceDigest: hash64,
      committedAt: '2026-10-02T09:00:00Z' }, 'receipt');
    expect(receipt.resultRevision).toBe(1);
    const lookup = decodeProcedureCommandLookup({ found: true, receipt }, 'lookup');
    expect(lookup.receipt?.evidenceDigest).toBe(hash64);
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



describe('Procedure execution and review workspace', () => {
  afterEach(() => TestBed.resetTestingModule());
  const basis = { procedureId: id2, sourceProcedureId: 'AWP-08-01', title: 'Cash confirmation',
    applicabilityStatus: 'APPLICABLE', procedureStatus: 'IN_PROGRESS', definitionRevision: 1,
    inputGeneration: 2, currentResultRevision: 0, latestResultId: null, latestResultRevision: 0,
    latestResultStatus: null, latestReviewId: null, latestReviewDecision: null, canSubmit: true,
    canReview: false, blockers: [] };
  const state = { basis, stateHash: hash64 };
  const preview = { requestId: id1, requestHash: hash64, action: 'SUBMIT_RESULT', basis, canProceed: true, blockers: [] };
  const receipt = { id: id3, procedureId: id2, sourceProcedureId: 'AWP-08-01', action: 'SUBMIT_RESULT',
    actorUserId: id1, actorEpoch: 1, requestId: id1, requestHash: hash64, definitionRevision: 1,
    inputGeneration: 2, expectedResultRevision: 0, resultId: id2, workpaperId: id3, reviewId: null,
    resultRevision: 1, resultStatus: 'SUBMITTED', decision: null, evidenceDigest: hash64,
    committedAt: '2026-10-02T09:00:00Z' };
  function open() {
    const workspace = signal({ engagementId: id1, canManageFieldwork: true, canViewReviewNotes: true, aggregate: null,
      risks: [], procedures: [{ id: id2, riskId: null, title: 'Cash confirmation', originalWording: null as string | null,
        effectiveWording: null as string | null, definitionRevision: null as number | null }] });
    const drafts = { readPendingRequest: vi.fn(() => ({ state: 'absent' })), read: vi.fn(() => ({ state: 'absent' })),
      save: vi.fn(() => true), clear: vi.fn(() => true) };
    const api = { resource: vi.fn(() => ({ data: workspace, loading: signal(false), error: signal(''), reload: vi.fn() })),
      command: vi.fn(), get: vi.fn() };
    TestBed.configureTestingModule({ providers: [{ provide: Api, useValue: api },
      { provide: SessionService, useValue: { current: signal({ userId: id1, firmId: id1, generation: '1', staff: true }), invalidation: signal(0) } },
      { provide: TabDrafts, useValue: drafts }, { provide: Router, useValue: { events: new Subject() } },
      { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ id: id1 })) } }] });
    const page = TestBed.runInInjectionContext(() => new AuditFieldwork()); TestBed.tick(); return { page, workspace, api, drafts };
  }
  it('appends controlled wording with source lineage, rationale and the current expected revision', async () => {
    const { page, workspace, api, drafts } = open();
    workspace.update(w => ({ ...w, procedures: [{ ...w.procedures[0], originalWording: 'Original controlled source wording.',
      effectiveWording: 'Existing effective instructions.', definitionRevision: 4 }] }));
    TestBed.tick();
    page.procedure.beginTailoring(workspace().procedures[0] as Parameters<FieldworkProcedureState['beginTailoring']>[0]);
    page.procedure.tailoringTitle = 'Cash confirmation';
    page.procedure.tailoringWording = 'Confirm the balance directly with the bank and investigate exceptions.';
    page.procedure.tailoringRationale = 'The engagement includes a material foreign-currency bank account.';
    api.command.mockResolvedValueOnce({ ok: true, value: {} });
    await page.procedure.submitTailoring();

    expect(api.command).toHaveBeenCalledWith(`/api/ui/procedures/${id2}/tailor`, expect.objectContaining({
      title: 'Cash confirmation', customWording: 'Confirm the balance directly with the bank and investigate exceptions.',
      rationale: 'The engagement includes a material foreign-currency bank account.', expectedDefinitionRevision: 4,
    }));
    expect((api.command.mock.calls[0][1] as { commandId: string }).commandId).toMatch(/^[0-9a-f-]{36}$/i);
    expect(workspace().procedures[0].originalWording).toBe('Original controlled source wording.');
    expect(drafts.save).toHaveBeenCalledWith(expect.objectContaining({ entity: `audit-procedure-tailoring/${id2}` }),
      expect.objectContaining({ expectedDefinitionRevision: 4 }), expect.any(Function), true);
    expect(drafts.clear).toHaveBeenCalledWith(`audit-procedure-tailoring/${id2}`);
  });
  it('retains and retries the same tailoring identity after an uncertain response', async () => {
    const { page, workspace, api } = open();
    workspace.update(w => ({ ...w, procedures: [{ ...w.procedures[0], originalWording: 'Original controlled source wording.',
      effectiveWording: 'Existing effective instructions.', definitionRevision: 2 }] }));
    TestBed.tick();
    page.procedure.beginTailoring(workspace().procedures[0] as Parameters<FieldworkProcedureState['beginTailoring']>[0]);
    page.procedure.tailoringTitle = 'Cash confirmation';
    page.procedure.tailoringWording = 'Confirm the bank balance at year end.';
    page.procedure.tailoringRationale = 'The account is material.';
    api.command.mockResolvedValueOnce({ ok: false, unknown: true, code: 'outcome.unknown', message: 'unknown', status: 0 })
      .mockResolvedValueOnce({ ok: true, value: {} });
    await page.procedure.submitTailoring();
    const original = api.command.mock.calls[0][1] as { commandId: string; title: string; customWording: string };
    expect(page.procedure.tailoringUncertain()).toBe(true);
    page.procedure.beginTailoring(workspace().procedures[0] as Parameters<FieldworkProcedureState['beginTailoring']>[0]);
    expect(page.procedure.tailoringPending()?.command.commandId).toBe(original.commandId);
    page.procedure.tailoringTitle = 'Changed while outcome unknown';
    await page.procedure.recoverTailoring();
    expect(api.command.mock.calls[1][1]).toEqual(api.command.mock.calls[0][1]);
    expect(api.command.mock.calls[1][1]).toMatchObject({ commandId: original.commandId, title: original.title, customWording: original.customWording });
    expect(page.procedure.tailoringUncertain()).toBe(false);
    expect(page.procedure.tailoringPending()).toBeNull();
  });
  it('does not send a tailoring command without a meaningful rationale', async () => {
    const { page, workspace, api } = open();
    workspace.update(w => ({ ...w, procedures: [{ ...w.procedures[0], originalWording: 'Original controlled source wording.',
      effectiveWording: 'Existing effective instructions.', definitionRevision: 1 }] }));
    TestBed.tick();
    page.procedure.beginTailoring(workspace().procedures[0] as Parameters<FieldworkProcedureState['beginTailoring']>[0]);
    page.procedure.tailoringWording = 'Updated instructions.';
    page.procedure.tailoringRationale = '   ';
    await page.procedure.submitTailoring();
    expect(api.command).not.toHaveBeenCalled();
    expect(page.procedure.tailoringFailed()).toBe(true);
  });
  it('requires preview and fresh assent, then recovers an unknown outcome from its exact receipt', async () => {
    const { page, workspace, api } = open();
    api.get.mockResolvedValueOnce({ procedureId: id2, currentResult: null, notes: [], evidence: [], physicalEvidence: [] })
      .mockResolvedValueOnce(state);
    await page.procedure.openProcedure(workspace().procedures[0] as Parameters<FieldworkProcedureState['openProcedure']>[0]);
    expect(page.procedure.procedureCommandState()?.basis.canSubmit).toBe(true);

    page.procedure.procedureWork = 'Agreed the bank listing to the ledger.';
    page.procedure.procedureConclusion = 'No exception noted.';
    api.command.mockImplementationOnce((_url: string, body: unknown) =>
      ({ ok: true, value: { ...preview, requestId: (body as { requestId: string }).requestId } }));
    await page.procedure.prepareProcedureCommand('SUBMIT_RESULT');
    expect(page.procedure.procedureCommandPreview()?.requestHash).toBe(hash64);
    const previewRequestId = page.procedure.procedureCommandPreview()!.requestId;
    expect(api.command.mock.calls[0][1]).toMatchObject({ action: 'SUBMIT_RESULT', reviewed: false });

    await page.procedure.executeProcedureCommand();
    expect(api.command).toHaveBeenCalledTimes(1);
    expect(page.procedure.procedureCommandReceipt()).toBeNull();

    page.procedure.procedureAssent = true;
    api.command.mockResolvedValueOnce({ ok: false, unknown: true, code: 'outcome.unknown', message: 'unknown', status: 0 });
    await page.procedure.executeProcedureCommand();
    expect(api.command).toHaveBeenCalledTimes(2);
    expect(api.command.mock.calls[1][1]).toMatchObject({ reviewed: true, expectedRequestHash: hash64 });
    expect(page.procedure.procedureUnknown()?.requestId).toBe(previewRequestId);

    api.get.mockResolvedValueOnce({ found: true, receipt });
    await page.procedure.recoverProcedureCommand();
    expect(page.procedure.procedureCommandReceipt()?.id).toBe(receipt.id);
    expect(page.procedure.procedureUnknown()).toBeNull();
  });
  it('retains an unknown exact receipt when its procedure selection changes in flight', async () => {
    const { page, workspace, api } = open();
    workspace.set({ ...workspace(), procedures: [{ ...workspace().procedures[0], id: id2 }, { ...workspace().procedures[0], id: id3 }] });
    api.get.mockImplementation(async (url: string) => url.includes('/commands/state') ? state :
      { procedureId: url.includes(id2) ? id2 : id3, currentResult: null, notes: [], evidence: [], physicalEvidence: [] });
    await page.procedure.openProcedure(workspace().procedures[0] as Parameters<FieldworkProcedureState['openProcedure']>[0]);
    page.procedure.procedureWork = 'Work.';
    page.procedure.procedureConclusion = 'Conclusion.';
    api.command.mockResolvedValueOnce({ ok: true, value: preview });
    await page.procedure.prepareProcedureCommand('SUBMIT_RESULT');
    page.procedure.procedureAssent = true;

    let complete!: (result: { ok: false; unknown: true; code: string; message: string; status: number }) => void;
    api.command.mockImplementationOnce(() => new Promise(resolve => complete = resolve));
    const pending = page.procedure.executeProcedureCommand();
    await page.procedure.selectProcedure(id3);
    complete({ ok: false, unknown: true, code: 'outcome.unknown', message: 'unknown', status: 0 });
    await pending;
    expect(page.procedure.procedureUnknownForSelection()).toBeNull();

    await page.procedure.openProcedure(workspace().procedures[0] as Parameters<FieldworkProcedureState['openProcedure']>[0]);
    expect(page.procedure.procedureUnknownForSelection()).toMatchObject({ procedureId: id2, requestHash: hash64 });
  });
  it('clears the preview when material inputs change after preview', async () => {
    const { page, workspace, api } = open();
    api.get.mockResolvedValueOnce({ procedureId: id2, currentResult: null, notes: [], evidence: [], physicalEvidence: [] })
      .mockResolvedValueOnce(state);
    await page.procedure.openProcedure(workspace().procedures[0] as Parameters<FieldworkProcedureState['openProcedure']>[0]);
    page.procedure.procedureWork = 'Work.';
    page.procedure.procedureConclusion = 'Conclusion.';
    api.command.mockResolvedValueOnce({ ok: true, value: preview });
    await page.procedure.prepareProcedureCommand('SUBMIT_RESULT');
    page.procedure.procedureAssent = true;
    page.procedure.procedureConclusion = 'Changed conclusion.';
    page.procedure.procedureInputChanged();
    expect(page.procedure.procedureCommandPreview()).toBeNull();
    expect(page.procedure.procedureAssent).toBe(false);
  });
  it('does not combine one procedure command state with another procedure review', async () => {
    const { page, workspace, api } = open();
    workspace.set({ ...workspace(), procedures: [
      { ...workspace().procedures[0], id: id2 }, { ...workspace().procedures[0], id: id3 },
    ] });
    api.get.mockImplementation(async (url: string) => url.includes('/commands/state') ? state :
      { procedureId: url.includes(id2) ? id2 : id3, currentResult: null, notes: [], evidence: [], physicalEvidence: [] });
    await page.procedure.openProcedure(workspace().procedures[0] as Parameters<FieldworkProcedureState['openProcedure']>[0]);
    expect(page.procedure.procedureSelectedId()).toBe(id2);

    await page.procedure.selectProcedure(id3);
    expect(page.procedure.procedureSelectedId()).toBe('');
    expect(page.procedure.procedureCommandState()).toBeNull();
    expect(page.procedure.review()?.procedureId).toBe(id3);
  });
});

describe('Audit fieldwork decimal inputs (T005)', () => {
  afterEach(() => TestBed.resetTestingModule());
  function open() {
    const workspace = signal({ engagementId: id1, canManageFieldwork: true, canViewReviewNotes: true, aggregate: null,
      risks: [], procedures: [{ id: id2, riskId: null }] });
    const api = { resource: vi.fn(() => ({ data: workspace, loading: signal(false), error: signal(''), reload: vi.fn() })),
      command: vi.fn(), get: vi.fn() };
    TestBed.configureTestingModule({ providers: [{ provide: Api, useValue: api },
      { provide: SessionService, useValue: { current: signal({ userId: id1, firmId: id1, generation: '1', staff: true }), invalidation: signal(0) } },
      { provide: TabDrafts, useValue: { readPendingRequest: () => ({ state: 'absent' }) } }, { provide: Router, useValue: { events: new Subject() } },
      { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ id: id1 })) } }] });
    const page = TestBed.runInInjectionContext(() => new AuditFieldwork()); TestBed.tick(); return { page, api };
  }
  function sampleData() {
    return decodeSampleSet({ selectionId: id1, procedureId: id2, method: 'MUS', rationale: 'Approved sample plan.', status: 'REVIEWED',
      selectedCount: 1, selectedSignedTotal: '100.00', inputGeneration: 1, testedCount: 0, exceptionCount: 0,
      cutOffRecordedCount: 0, subsequentMatchedCount: 0, totalCount: 1, page: 1, pageSize: 100,
      items: [{ selectionItemId: id3, stableRowId: 'ROW-001', signedAmount: '100.00', currency: 'QAR', inclusionReason: 'MUS interval',
        testResult: 'PENDING', testRevision: 0, exceptionAmount: null, auditItemTestId: null, testWorkPerformed: null,
        evidenceReferences: [], contradictoryEvidence: null, followUp: null, testReviewed: false,
        testReviewDecision: null, testReviewComment: null, canReviewTest: false }] }, 'sampleSet');
  }

  it('transmits six-place exception amounts, refuses invalid nonblank amounts, and keeps blank optional amounts null', async () => {
    const { page, api } = open();
    const set = sampleData(), item = set.items[0];
    api.command.mockResolvedValue({ ok: true, value: null });

    page.sampling.sampleSet.set(set);
    Object.assign(page.sampling.itemDraft(item), { workPerformed: 'Agreed to the source document.', evidenceReferences: 'evidence-1',
      result: 'EXCEPTION', exceptionAmount: '123.456789', followUp: 'Investigate the difference.' });
    await page.sampling.recordItemTest(item);
    expect(api.command).toHaveBeenCalledTimes(1);
    expect(api.command.mock.calls[0][1]).toMatchObject({ exceptionAmount: '123.456789' });

    page.sampling.sampleSet.set(set);
    Object.assign(page.sampling.itemDraft(item), { workPerformed: 'Agreed to the source document.', evidenceReferences: 'evidence-1',
      result: 'EXCEPTION', exceptionAmount: '123.4567890', followUp: 'Investigate the difference.' });
    await page.sampling.recordItemTest(item);
    expect(api.command).toHaveBeenCalledTimes(1);

    page.sampling.sampleSet.set(set);
    Object.assign(page.sampling.itemDraft(item), { workPerformed: 'Agreed to the source document.', evidenceReferences: 'evidence-1',
      result: 'EXCEPTION', exceptionAmount: '  ', followUp: 'Investigate the difference.' });
    await page.sampling.recordItemTest(item);
    expect(api.command).toHaveBeenCalledTimes(2);
    expect(api.command.mock.calls[1][1]).toMatchObject({ exceptionAmount: null });
  });

  it('preserves six-place sampling intervals and thresholds without changing sampling behavior', async () => {
    const { page, api } = open();
    const preview = (method: string, interval: string | null, keyItemThreshold: string | null) => ({ previewDigest: hash64, sourceDigest: hash64,
      outcome: { method, populationCount: 1, populationSignedTotal: '12.345678', populationAbsoluteTotal: '12.345678',
        selectedCount: 1, selectedAbsoluteTotal: '12.345678', coveragePercent: '100.000000', seed: null, interval, keyItemThreshold,
        items: [{ stableRowId: 'ROW-001', signedAmount: '12.345678', absoluteAmount: '12.345678', inclusionReason: 'Selected',
          cumulativeAbsoluteAmount: '12.345678' }], strata: null } });
    Object.assign(page.sampling.s, { procedure: id2, schedule: id3, method: 'MUS', interval: '12.345678', key: '', rationale: 'Approved plan.' });
    api.command.mockResolvedValueOnce({ ok: true, value: preview('MUS', '12.345678', null) });
    await page.sampling.sample();
    expect(api.command.mock.calls[0][1]).toMatchObject({ interval: '12.345678', keyItemThreshold: null });
    expect(page.sampling.previewMatchesInputs()).toBe(true);

    Object.assign(page.sampling.s, { method: 'KEY_ITEM', interval: '', key: '0.123456' });
    api.command.mockResolvedValueOnce({ ok: true, value: preview('KEY_ITEM', null, '0.123456') });
    await page.sampling.sample();
    expect(api.command.mock.calls[1][1]).toMatchObject({ interval: null, keyItemThreshold: '0.123456' });
    expect(page.sampling.previewMatchesInputs()).toBe(true);

    page.sampling.inputsChanged();
    expect(page.sampling.samplingPreview()).toBeNull();
    expect(page.sampling.previewMatchesInputs()).toBe(false);

    Object.assign(page.sampling.s, { method: 'MUS', interval: '12.3456789' });
    await page.sampling.sample();
    expect(api.command).toHaveBeenCalledTimes(2);
    expect(page.sampling.previewMatchesInputs()).toBe(false);

    let completePreview!: (result: { ok: true; value: unknown }) => void;
    api.command.mockImplementationOnce(() => new Promise(resolve => completePreview = resolve));
    Object.assign(page.sampling.s, { method: 'MUS', interval: '12.345678', rationale: 'Approved plan.' });
    const pendingPreview = page.sampling.sample();
    page.sampling.s.seed = 100;
    page.sampling.inputsChanged();
    completePreview({ ok: true, value: preview('MUS', '12.345678', null) });
    await pendingPreview;
    expect(page.sampling.samplingPreview()).toBeNull();
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
    const older = page.procedure.selectProcedure(id2), newer = page.procedure.selectProcedure(id3);
    second({ procedureId: id3 }); await newer;
    first({ procedureId: id2 }); await older;
    expect(page.procedure.review()?.procedureId).toBe(id3);
  });
  it('discards review results after scope refresh and rejects mismatched procedure identity', async () => {
    const { page, workspace, api } = open(); let complete!: (value: unknown) => void;
    api.get.mockImplementationOnce(() => new Promise(resolve => complete = resolve));
    const pending = page.procedure.selectProcedure(id2);
    workspace.set({ ...workspace() }); TestBed.tick(); complete({ procedureId: id2 }); await pending;
    expect(page.procedure.review()).toBeNull();
    api.get.mockResolvedValueOnce({ procedureId: id3 }); await page.procedure.selectProcedure(id2);
    expect(page.procedure.review()).toBeNull(); expect(page.procedure.reviewError()).toBe('Procedure review unavailable.');
  });
  it('does not publish a pending procedure review after destruction', async () => {
    const { page, api } = open(); let complete!: (value: unknown) => void;
    api.get.mockImplementationOnce(() => new Promise(resolve => complete = resolve));
    const pending = page.procedure.selectProcedure(id2); TestBed.resetTestingModule();
    complete({ procedureId: id2 }); await pending; expect(page.procedure.review()).toBeNull();
  });
  it('does not let an old engagement command overwrite current feedback or reload the new scope', async () => {
    const { page, api } = open();
    const reload = vi.spyOn(page.ws, 'reload');
    let completeOld!: (value: unknown) => void;
    api.command.mockImplementationOnce(() => new Promise(resolve => completeOld = resolve));
    const oldCommand = page.send(`/api/ui/engagements/${id1}/fieldwork/physical`, {}, 'Old engagement completed.');
    const route = TestBed.inject(ActivatedRoute);
    (route.paramMap as BehaviorSubject<ReturnType<typeof convertToParamMap>>).next(convertToParamMap({ id: id3 }));
    TestBed.tick();
    expect(page.id()).toBe(id3);
    expect(page.previousScopeOutcomeUncertain()).toBe(true);

    api.command.mockResolvedValueOnce({ ok: true, value: {} });
    await page.send(`/api/ui/engagements/${id3}/fieldwork/physical`, {}, 'Current engagement completed.');
    expect(page.cmd.message()).toBe('Current engagement completed.');
    completeOld({ ok: true, value: {} });
    await oldCommand;

    expect(page.cmd.message()).toBe('Current engagement completed.');
    expect(page.previousScopeOutcomeUncertain()).toBe(false);
    expect(reload).toHaveBeenCalledTimes(1);
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
