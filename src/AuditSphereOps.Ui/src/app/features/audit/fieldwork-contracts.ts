import { arr, bool, date, dec, DecodeError, Decoder, guid, instant, int, nat, nullable, obj, oneOf, sha256, text } from '../../core/decode';

export type TailorProcedureCommand = {
  title: string;
  customWording: string;
  rationale: string;
  expectedDefinitionRevision: number;
  commandId: string;
};

export function decodeTailorProcedureCommand(raw: unknown): TailorProcedureCommand | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const value = raw as Record<string, unknown>;
  if (typeof value['title'] !== 'string' || !value['title'].trim() || value['title'].length > 500 ||
      typeof value['customWording'] !== 'string' || !value['customWording'].trim() || value['customWording'].length > 2000 ||
      typeof value['rationale'] !== 'string' || !value['rationale'].trim() || value['rationale'].length > 1000 ||
      !Number.isInteger(value['expectedDefinitionRevision']) || (value['expectedDefinitionRevision'] as number) < 1 ||
      typeof value['commandId'] !== 'string' || !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value['commandId'])) return null;
  return { title: value['title'], customWording: value['customWording'], rationale: value['rationale'],
    expectedDefinitionRevision: value['expectedDefinitionRevision'] as number, commandId: value['commandId'] };
}

const procedure = obj({ id: guid, sourceProcedureId: text, sourceSectionNumber: nullable(int), sourceSectionTitle: nullable(text), title: text,
  applicabilityStatus: text, status: text, riskId: nullable(guid), workpaperId: nullable(guid), originalWording: nullable(text),
  effectiveWording: nullable(text), definitionRevision: nullable(nat), definitionProvenance: text,
  applicabilityRevision: nat, applicabilityDecisionId: nullable(guid), pendingApplicabilityProposalId: nullable(guid),
  applicabilityRationale: nullable(text), applicabilityHistory: arr(obj({ id: guid, revision: nat, action: text,
    rationale: nullable(text), proposalDecisionId: nullable(guid), actorUserId: guid, occurredAt: instant,
    definitionRevision: nat, inputGeneration: nat }), 20), canProposeNotApplicable: bool,
  canApproveNotApplicable: bool, canReturnNotApplicable: bool, evidenceState: text,
  supportLinks: arr(obj({ toolKind: text, label: text, route: nullable(text), explanation: text, availability: text }), 20) });
export const decodeManagerReview = obj({ engagementId: guid, completionReady: bool, partnerHandoffReady: bool,
  applicableProcedures: nat, proceduresNeedingReview: nat, itemTestsNeedingReview: nat, selectionsNeedingReview: nat,
  unresolvedReviewThreads: nat, openConfirmations: nat, bankReconciliationsNeedingApproval: nat, unevaluatedDifferences: nat,
  summaryReviewMemorandumState: text, summaryReviewMemorandumVersion: nullable(nat),
  summaryReviewMemorandumGeneratedAt: nullable(instant), blockers: arr(obj({ code: text, label: text, count: nat }), 100) });
const areaSections: Record<string, number> = {
  CASH_BANK: 2, RECEIVABLES: 3, INVENTORY: 4, REVENUE: 5, PAYABLES: 6, FIXED_ASSETS: 7,
  EXPENSES: 8, PAYROLL: 9, LOANS: 10, EQUITY: 11, RELATED_PARTIES: 12, TAX_STATUTORY: 13,
  JOURNALS_FRAUD: 14, ANALYTICAL_REVIEW: 15, GOING_CONCERN: 16, SUBSEQUENT_EVENTS: 17,
  FINANCIAL_STATEMENTS: 18, AUDIT_DIFFERENCES: 19,
};
const areaSummaryShape = obj({ engagementId: guid, areaCode: text, sectionNumber: int, sectionTitle: text,
  procedureCount: nat, applicableProcedureCount: nat, reviewedProcedureCount: nat,
  procedures: arr(obj({ procedureId: guid, sourceProcedureId: text, status: text, applicabilityStatus: text }), 5000),
  assessments: arr(obj({ assessmentId: guid, assessmentKind: text, conclusion: text, status: text,
    bookedAmount: nullable(dec), auditedAmount: nullable(dec), residualAmount: nullable(dec), currency: nullable(text), revision: nat,
    preparedByMe: bool }), 1000),
  scheduleCount: nat, approvedScheduleCount: nat, selectionCount: nat, selectedItemCount: nat, testedItemCount: nat,
  exceptionItemCount: nat, cutOffRecordedCount: nat, cutOffExceptionCount: nat, subsequentRecordedCount: nat,
  subsequentMatchedCount: nat, confirmationCount: nat, confirmationClosedCount: nat,
  differences: arr(obj({ differenceId: guid, differenceType: text, amount: dec, currency: text, status: text,
    correctionState: nullable(text) }), 5000), workpapersReviewed: bool, hasBlockingEvidence: bool });
export const decodeAreaSummary: Decoder<ReturnType<typeof areaSummaryShape>> = (raw, path) => {
  const value = areaSummaryShape(raw, path);
  if (areaSections[value.areaCode] !== value.sectionNumber || value.applicableProcedureCount > value.procedureCount ||
      value.reviewedProcedureCount > value.procedureCount || value.approvedScheduleCount > value.scheduleCount ||
      value.selectedItemCount < value.testedItemCount || value.testedItemCount < value.exceptionItemCount ||
      value.subsequentMatchedCount > value.subsequentRecordedCount || value.confirmationClosedCount > value.confirmationCount ||
      new Set(value.procedures.map(x => x.procedureId)).size !== value.procedures.length ||
      new Set(value.assessments.map(x => x.assessmentId)).size !== value.assessments.length ||
      new Set(value.differences.map(x => x.differenceId)).size !== value.differences.length)
    throw new DecodeError(`${path}: inconsistent audit area summary`);
  return value;
};
export type AreaSummary = ReturnType<typeof decodeAreaSummary>;
export const decodeFieldwork = obj({ engagementId: guid, catalogProcedureCount: nat, catalogVersion: text,
  program: nullable(obj({ programCode: text, version: text, sourceHash: text })), procedures: arr(procedure, 5000),
  risks: arr(obj({ id: guid, area: text, significanceDecision: text, band: nullable(text), effectiveBand: nullable(text),
    balance: nullable(dec), currency: nullable(text), tolerableError: nullable(dec), planningMateriality: nullable(dec),
    riskAssessmentId: nullable(guid), materialityAssessmentId: nullable(guid), materialityCalculationId: nullable(guid),
    mappingVersionId: nullable(guid), mappingVersionNumber: nullable(nat), datasetId: nullable(guid), datasetDigest: nullable(text),
    destinationCode: nullable(text), statementSection: nullable(text), ruleVersion: nullable(text),
    explanation: nullable(text), blocker: nullable(text) }), 1000),
  differences: arr(obj({ currency: text, differenceCount: nat, grossAmount: dec, signedNetAmount: dec, unadjustedGrossAmount: dec, unadjustedSignedNetAmount: dec,
    correctedGrossAmount: dec, correctedSignedNetAmount: dec }), 100),
  aggregate: nullable(obj({ id: guid, status: text, conclusion: text, preparedByMe: bool })), aggregateCurrentAndReviewed: bool,
  schedules: arr(obj({ id: guid, scheduleType: text, rowCount: nat, currency: text }), 1000), samplingMethods: arr(text, 20),
  samplingRuns: arr(obj({ id: guid, selectionId: guid, selectionStatus: text, preparedByMe: bool, canReviewSelection: bool,
    createdAt: instant, method: text, interval: nullable(dec), keyItemThreshold: nullable(dec), sampleSize: nullable(int), seed: nullable(int),
    selectedCount: nat, populationCount: nat, coveragePercent: dec, sourceDigest: text, selectionDigest: text, engineVersion: text, previewDigest: nullable(text),
    orderingPolicy: nullable(text), attributeFields: arr(text, 8), reproduces: bool }), 10),
  evidenceCandidates: arr(obj({ uploadIntentId: guid, pbcRequestId: guid, requestArea: text, fileName: text, contentSha256: text, receivedAt: instant, superseded: bool }), 5000),
  physicalItems: arr(obj({ id: guid, fileIndex: text, boxReference: text, description: text, currentLocation: text,
    movements: arr(obj({ toLocation: text, movedAt: instant }), 1000), procedureTitles: arr(text, 1000) }), 5000),
  canManageFieldwork: bool, canReviewSelections: bool, canViewReviewNotes: bool, canAddOrResolveReviewNotes: bool, canRespondToReviewNotes: bool });
export const decodeProgramOptions = obj({ engagementId: guid, currentAdoptionId: nullable(guid), currentProgramVersionId: nullable(guid),
  currentVersion: nullable(text), currentSourceHash: nullable(text), currentContentDigest: nullable(text), baselineProcedureCount: nat,
  adHocProcedureCount: nat, currentAdoptionComplete: bool, currentAdoptionBlocker: nullable(text),
  options: arr(obj({ programVersionId: guid, programCode: text, version: text, sourceHash: text, contentDigest: nullable(text),
    procedureCount: nat, eligible: bool, blocker: nullable(text) }), 50) });
export const samplingPreviewDecoder = obj({ previewDigest: text, sourceDigest: text, outcome: obj({ method: text, populationCount: nat, populationSignedTotal: dec,
  populationAbsoluteTotal: dec, selectedCount: nat, selectedAbsoluteTotal: dec, coveragePercent: dec, seed: nullable(int), interval: nullable(dec),
  keyItemThreshold: nullable(dec), items: arr(obj({ stableRowId: text, signedAmount: dec, absoluteAmount: dec, inclusionReason: text,
    cumulativeAbsoluteAmount: dec }), 100000),
  strata: nullable(arr(obj({ stratumKey: text, populationCount: nat, slots: nat, selectedCount: nat }), 10000)) }) });
export const decodeProcedureReview = obj({ procedureId: guid,
  currentResult: nullable(obj({ id: guid, revision: nat, workPerformed: text, conclusion: nullable(text),
    submittedEvidenceJson: nullable(text), evidenceDigest: nullable(text), evidenceReferencesJson: nullable(text) })),
  notes: arr(obj({ noteId: guid, resultRevision: nat, field: text, excerpt: text, startOffset: int, body: text, authorName: text, createdAt: instant, open: bool,
    events: arr(obj({ id: guid, kind: text, body: text, createdAt: instant }), 500) }), 500),
  evidence: arr(obj({ linkId: guid, uploadIntentId: guid, fileName: text, contentSha256: text, note: nullable(text), linkedAt: instant }), 1000),
  physicalEvidence: arr(obj({ linkId: guid, itemId: guid, fileIndex: text, boxReference: text, description: text,
    currentLocation: text, linkedAt: instant }), 1000) });
const procedureCommandBasis = obj({ procedureId: guid, sourceProcedureId: text, title: text, applicabilityStatus: text,
  procedureStatus: text, definitionRevision: nat, inputGeneration: nat, currentResultRevision: nat,
  latestResultId: nullable(guid), latestResultRevision: nat, latestResultStatus: nullable(text),
  latestReviewId: nullable(guid), latestReviewDecision: nullable(text), canSubmit: bool, canReview: bool,
  blockers: arr(text, 100) });
export const decodeProcedureCommandState = obj({ basis: procedureCommandBasis, stateHash: text });
export const decodeProcedureCommandPreview = obj({ requestId: guid, requestHash: text, action: text,
  basis: procedureCommandBasis, canProceed: bool, blockers: arr(text, 100) });
export const decodeProcedureCommandReceipt = obj({ id: guid, procedureId: guid, sourceProcedureId: text, action: text,
  actorUserId: guid, actorEpoch: nat, requestId: guid, requestHash: text, definitionRevision: nat,
  inputGeneration: nat, expectedResultRevision: nat, resultId: nullable(guid), workpaperId: nullable(guid),
  reviewId: nullable(guid), resultRevision: nat, resultStatus: nullable(text), decision: nullable(text),
  evidenceDigest: nullable(text), committedAt: instant });
export const decodeProcedureCommandLookup = obj({ found: bool, receipt: nullable(decodeProcedureCommandReceipt) });
export type Fieldwork = ReturnType<typeof decodeFieldwork>;
export type SamplingPreview = ReturnType<typeof samplingPreviewDecoder>;
export const decodeSampleSet = obj({ selectionId: guid, procedureId: guid, method: text, rationale: text, status: text,
  selectedCount: nat, selectedSignedTotal: dec, inputGeneration: nat, testedCount: nat, exceptionCount: nat, cutOffRecordedCount: nat,
  subsequentMatchedCount: nat, totalCount: nat, page: nat, pageSize: nat,
  items: arr(obj({ selectionItemId: guid, stableRowId: text, signedAmount: dec, currency: text, inclusionReason: text,
    testResult: text, testRevision: nat, exceptionAmount: nullable(dec), auditItemTestId: nullable(guid), testWorkPerformed: nullable(text),
    evidenceReferences: arr(text, 100), contradictoryEvidence: nullable(text), followUp: nullable(text), testReviewed: bool,
    testReviewDecision: nullable(text), testReviewComment: nullable(text), canReviewTest: bool,
    cutOffTestId: nullable(guid), cutOffIndicator: nullable(text), cutOffException: nullable(bool), cutOffRevision: nat,
    subsequentMatchId: nullable(guid), subsequentState: nullable(text), matchedAmount: nullable(dec), subsequentRevision: nat }), 500) });
export type SampleSet = ReturnType<typeof decodeSampleSet>;
export type SampleItem = SampleSet['items'][number];
export type SampleTestDraft = { workPerformed: string; evidenceReferences: string; result: string; exceptionAmount: string;
  contradictoryEvidence: string; followUp: string; reviewerComment: string };
export type SampleEvidenceDraft = { periodEndDate: string; transactionDate: string; documentDate: string; shipReceiveDate: string;
  workPerformed: string; evidenceReferences: string; cutOffExpectedRevision: number; matchedAmount: string; sourceReference: string;
  subsequentExpectedRevision: number; subsequentDate: string; evidenceReference: string; unmatchedReason: string };

const sourceLong = (value: unknown, path: string): string => {
  if (typeof value !== 'string' || !/^(0|[1-9][0-9]{0,18})$/.test(value) || BigInt(value) > 9223372036854775807n)
    throw new DecodeError(`${path}: expected canonical non-negative 64-bit integer string`);
  return value;
};
const sourcePositiveLong = (value: unknown, path: string): string => {
  const parsed = sourceLong(value, path);
  if (parsed === '0') throw new DecodeError(`${path}: expected positive integer`);
  return parsed;
};
const sourceFields = obj({ pbcAcceptanceReceiptId: guid, acceptedSourceDecisionId: guid, sourceDate: date,
  columns: obj({ accountCode: text, description: text, signedAmount: text, currency: text,
    stableRowId: nullable(text), transactionDate: nullable(text), postingDate: nullable(text),
    deliveryDate: nullable(text), serviceDate: nullable(text) }),
  selectedControlAccountCodes: arr(text, 20000) });
export const decodeScheduleSourceOptions = obj({
  pbcSources: arr(obj({ receiptId: guid, fileName: text, byteCount: nat, entityScope: text, periodStart: date, periodEnd: date }), 100),
  nextPbcOffset: nat, hasMorePbcSources: bool,
  trialBalances: arr(obj({ decisionId: guid, periodId: guid, bookId: nullable(guid), basis: nullable(text),
    periodStart: date, periodEnd: date, legalEntityKey: text, currency: text,
    datasetRevision: sourcePositiveLong, inputGeneration: sourcePositiveLong }), 100),
  nextTrialBalanceOffset: nat, hasMoreTrialBalances: bool,
});
export const decodeScheduleSourceHeaders = obj({ receiptId: guid, profileVersion: text, headers: arr(text, 256) });
const scheduleControlAccountPage = obj({ offset: nat, limit: int, hasMore: bool,
  items: arr(obj({ accountCode: text, accountName: text, balance: dec, currency: text }), 100) });
export function decodeScheduleControlAccountPage(raw: unknown, path: string) {
  const page = scheduleControlAccountPage(raw, path);
  if (page.limit < 1 || page.limit > 100 || page.items.length > page.limit ||
      page.items.some(x => !x.accountCode.trim() || x.accountCode !== x.accountCode.trim()) ||
      new Set(page.items.map(x => x.accountCode)).size !== page.items.length)
    throw new DecodeError(`${path}: invalid bounded account page`);
  return page;
}
export const decodeScheduleCapturePreview = obj({ requestId: guid, requestHash: sha256, manifestHash: sha256,
  inputGeneration: sourcePositiveLong, periodRevision: sourcePositiveLong, canonicalInput: sourceFields,
  fileName: text, rawFileSha256: sha256, rowCount: sourcePositiveLong, currency: text, legalEntityKey: text,
  sourceDate: date, signedTotal: dec, closingControl: dec, residual: dec, hasCompleteListingCoverage: bool,
  allAccountsReconciled: bool, accounts: arr(obj({ accountCode: text, listingAmount: dec, trialBalanceAmount: dec,
    difference: dec, listingRowCount: sourceLong, trialBalanceRowCount: sourceLong }), 20000) });
const scheduleReviewCandidatePage = obj({ offset: nat, limit: int, hasMore: bool,
  items: arr(obj({ scheduleId: guid, sourceDate: date, fileName: text, scheduleStatus: text, capturedAt: instant }), 100) });
export function decodeScheduleReviewCandidates(raw: unknown, path: string) {
  const page = scheduleReviewCandidatePage(raw, path);
  if (page.limit < 1 || page.limit > 100 || page.items.length > page.limit ||
      new Set(page.items.map(x => x.scheduleId)).size !== page.items.length)
    throw new DecodeError(`${path}: invalid bounded review-candidate page`);
  return page;
}
const scheduleSourceReviewPreview = obj({ requestId: guid, scheduleId: guid, captureHash: sha256,
  inputGeneration: sourcePositiveLong, reviewBasis: sha256, requestHash: sha256,
  canonicalInput: obj({ action: oneOf('APPROVE', 'RETURN'), reason: text, evidenceReference: text }),
  sourceCurrent: bool, canApprove: bool, sourceFailureCode: nullable(text), status: text,
  existingReviewId: nullable(guid), source: obj({ scheduleType: text, pbcAcceptanceReceiptId: guid,
    acceptedSourceDecisionId: guid, trialBalanceDatasetId: guid, datasetRevision: sourcePositiveLong,
    periodId: guid, periodCode: text, periodStart: date, periodEnd: date, periodRevision: sourcePositiveLong,
    bookId: nullable(guid), bookRevision: nullable(sourcePositiveLong), basis: nullable(text),
    legalEntityKey: text, currency: text, sourceDate: date, fileName: text, profileVersion: text,
    rawFileSha256: sha256, byteCount: nat, rowCount: nat, controlBasis: text,
    selectedControlAccountCodes: arr(text, 20000), signedTotal: dec, closingControl: dec, residual: dec,
    hasCompleteListingCoverage: bool, allAccountsReconciled: bool,
    accounts: arr(obj({ accountCode: text, listingAmount: dec, trialBalanceAmount: dec, difference: dec,
      listingRowCount: sourceLong, trialBalanceRowCount: sourceLong }), 20000) }) });
export function decodeScheduleSourceReviewPreview(raw: unknown, path: string) {
  const preview = scheduleSourceReviewPreview(raw, path), source = preview.source;
  if (source.rowCount < 1 || source.byteCount < 1 || source.periodStart > source.periodEnd ||
      source.sourceDate !== source.periodEnd || source.profileVersion !== 'audit-client-listing-csv.v1' ||
      source.selectedControlAccountCodes.length < 1 ||
      new Set(source.selectedControlAccountCodes).size !== source.selectedControlAccountCodes.length ||
      source.selectedControlAccountCodes.some(code => !code.trim() || code !== code.trim()) ||
      source.accounts.length !== source.selectedControlAccountCodes.length ||
      source.accounts.some((account, index) => account.accountCode !== source.selectedControlAccountCodes[index]) ||
      source.allAccountsReconciled && !source.hasCompleteListingCoverage ||
      preview.canApprove && (!preview.sourceCurrent || !source.hasCompleteListingCoverage || !source.allAccountsReconciled))
    throw new DecodeError(`${path}: inconsistent captured source review evidence`);
  return preview;
}
const scheduleReviewSourceRows = obj({ captureHash: sha256, offset: nat, limit: int, totalRows: nat,
  hasMore: bool, headers: arr(text, 256),
  items: arr(obj({ stableRowId: text, sourceLineNumber: int, cells: arr(text, 256) }), 100) });
export function decodeScheduleReviewSourceRows(raw: unknown, path: string) {
  const page = scheduleReviewSourceRows(raw, path);
  if (page.limit < 1 || page.limit > 100 || page.items.length > page.limit ||
      page.offset > page.totalRows || page.items.length !== Math.min(page.limit, page.totalRows - page.offset) ||
      page.headers.length < 1 || page.headers.some(header => !header.trim()) ||
      new Set(page.headers).size !== page.headers.length ||
      page.hasMore !== (page.offset + page.items.length < page.totalRows) ||
      page.items.some(row => row.sourceLineNumber < 2 ||
        !row.stableRowId || row.cells.length !== page.headers.length) ||
      new Set(page.items.map(row => row.stableRowId)).size !== page.items.length)
    throw new DecodeError(`${path}: invalid retained schedule source page`);
  return page;
}
const sourceReceipt = obj({ version: text, action: oneOf('CAPTURE', 'APPROVE', 'RETURN'), scheduleId: guid,
  reviewId: nullable(guid), firmId: guid, clientId: guid, engagementId: guid, actorUserId: guid,
  actorEpoch: sourcePositiveLong, requestId: guid, requestHash: sha256, manifestHash: sha256,
  inputGeneration: sourcePositiveLong, rowCount: sourcePositiveLong, signedTotal: dec, closingControl: dec,
  residual: dec, status: text, createdAt: instant });
export const decodeScheduleSourceReceiptView = obj({ receipt: sourceReceipt, receiptHash: sha256 });
export const decodeScheduleSourceReceiptLookup = obj({ found: bool, receipt: nullable(decodeScheduleSourceReceiptView) });
export type ScheduleSourceOptions = ReturnType<typeof decodeScheduleSourceOptions>;
export type ScheduleSourceHeaders = ReturnType<typeof decodeScheduleSourceHeaders>;
export type ScheduleControlAccountPage = ReturnType<typeof decodeScheduleControlAccountPage>;
export type ScheduleCapturePreview = ReturnType<typeof decodeScheduleCapturePreview>;
export type ScheduleReviewCandidate = ReturnType<typeof decodeScheduleReviewCandidates>['items'][number];
export type ScheduleSourceReviewPreview = ReturnType<typeof decodeScheduleSourceReviewPreview>;
export type ScheduleReviewSourceRowsPage = ReturnType<typeof decodeScheduleReviewSourceRows>;
export type ScheduleSourceReceiptLookup = ReturnType<typeof decodeScheduleSourceReceiptLookup>;

/** Risks recorded against one statement line (STE-NXT-013). The line is the only key the link sends. */
export function focusRisks<T extends { destinationCode: string | null }>(risks: readonly T[], line: string | null): T[] {
  if (!line) return [];
  return risks.filter((risk) => risk.destinationCode === line);
}


// Read-only retained bank evidence. Generation equality does not grant review or acceptance.
const bankGeneration = (value: unknown, path: string): string => {
  if (typeof value !== 'string' || !/^[1-9][0-9]{0,18}$/.test(value) || BigInt(value) > 9223372036854775807n)
    throw new DecodeError(`${path}: expected canonical positive generation`);
  return value;
};
const bankGenerationState = oneOf('CURRENT_GENERATION', 'STALE_GENERATION', 'UNAVAILABLE');
const bankRow = obj({ id: guid, procedureId: nullable(guid), asOfDate: date, currency: text,
  status: oneOf('RECONCILED', 'UNRECONCILED', 'APPROVED', 'CHANGES_REQUIRED'), ledgerBalance: dec,
  statementBalance: dec, timingItemTotal: dec, proposedCorrectionTotal: dec, residual: dec,
  inputGeneration: bankGeneration, generationState: bankGenerationState, conclusion: nullable(text),
  createdByUserId: guid, createdAt: instant, reviewedByUserId: nullable(guid), reviewedAt: nullable(instant) });
const bankSchedule = obj({ id: guid, scheduleType: text, entityIdentifier: text, sourceReceiptReference: text,
  asOfDate: nullable(instant), periodStart: nullable(date), periodEnd: nullable(date), currency: text,
  signConvention: text, sourceHash: text, sourceImportBatchId: nullable(guid), acceptedSourceDecisionId: nullable(guid),
  acceptedSourceHash: text, rowCount: nat, signedControlTotal: dec, glControlTotal: dec, residual: dec,
  inputGeneration: bankGeneration, generationState: bankGenerationState, status: text });
const bankItem = obj({ id: guid, stableItemId: text,
  itemType: oneOf('LEDGER', 'STATEMENT', 'TIMING', 'PROPOSED_CORRECTION'), signedAmount: dec, currency: text,
  description: text, sourceReference: text, evidenceReference: text, sourceScheduleId: nullable(guid), proposedJournalId: nullable(guid) });
const bankPage = obj({ engagementId: guid, currentInputGeneration: nullable(bankGeneration), offset: nat,
  limit: nat, total: nat, rows: arr(bankRow, 25) });
const bankDetail = obj({ engagementId: guid, currentInputGeneration: nullable(bankGeneration), reconciliation: bankRow,
  ledgerScheduleId: guid, statementScheduleId: guid, ledgerSchedule: nullable(bankSchedule), statementSchedule: nullable(bankSchedule),
  offset: nat, limit: nat, total: nat, items: arr(bankItem, 100) });
const bankCommandItem = obj({ id: guid, stableItemId: text, itemType: oneOf('LEDGER', 'STATEMENT', 'TIMING', 'PROPOSED_CORRECTION'),
  signedAmount: dec, currency: text, description: text, sourceReference: text, evidenceReference: text,
  sourceScheduleId: nullable(guid), proposedJournalId: nullable(guid) });
const bankCommandRecord = obj({ id: guid, clientId: guid, engagementId: guid, procedureId: nullable(guid),
  ledgerScheduleId: guid, statementScheduleId: guid, asOfDate: date, currency: text, ledgerBalance: dec,
  statementBalance: dec, timingItemTotal: dec, proposedCorrectionTotal: dec, residual: dec,
  status: oneOf('RECONCILED', 'UNRECONCILED', 'APPROVED', 'CHANGES_REQUIRED'), inputGeneration: bankGeneration,
  conclusion: nullable(text), createdByUserId: guid, reviewedByUserId: nullable(guid), createdAt: instant,
  reviewedAt: nullable(instant), items: arr(bankCommandItem, 1000) });
const bankSourceOption = obj({ id: guid, scheduleType: oneOf('BANK_LEDGER', 'BANK_STATEMENT'), entityIdentifier: text,
  asOfDate: nullable(date), currency: text, signedControlTotal: dec, sourceHash: sha256, status: text, inputGeneration: bankGeneration });
const bankValue = obj({ bankReconciliationId: guid, status: oneOf('RECONCILED', 'UNRECONCILED', 'APPROVED', 'CHANGES_REQUIRED'),
  currency: text, ledgerBalance: dec, statementBalance: dec, timingItemTotal: dec, proposedCorrectionTotal: dec, residual: dec });
const bankCommandPreview = obj({ requestId: guid, action: oneOf('CREATE', 'REVIEW'), requestHash: sha256,
  reviewBasis: sha256, expectedGeneration: bankGeneration, canProceed: bool, blockers: arr(text, 50),
  currentRecord: nullable(bankCommandRecord), calculated: nullable(bankValue), summary: text });
const bankCommandReceipt = obj({ id: guid, clientId: guid, engagementId: guid, bankReconciliationId: guid,
  actorId: guid, actorEpoch: bankGeneration, requestId: guid, requestHash: sha256, reviewBasis: sha256,
  action: oneOf('CREATE', 'REVIEW'), createdAt: instant, preview: bankCommandPreview, result: bankCommandRecord });
export const decodeBankReconciliationCommandState = obj({ engagementId: guid, clientId: guid,
  expectedGeneration: bankGeneration, reviewBasis: sha256, canCreate: bool, canReview: bool,
  sources: arr(bankSourceOption, 200), record: nullable(bankCommandRecord), blockers: arr(text, 50) });
export const decodeBankReconciliationCommandPreview = bankCommandPreview;
export const decodeBankReconciliationCommandReceipt = bankCommandReceipt;
export const decodeBankReconciliationCommandLookup = (raw: unknown, path: string) => {
  const result = obj({ found: bool, receipt: nullable(bankCommandReceipt) })(raw, path);
  if (result.found !== !!result.receipt) throw new DecodeError(`${path}: inconsistent receipt lookup`);
  return result;
};
function requireBankPage(offset: number, limit: number, total: number, count: number, max: number, path: string): void {
  if (limit < 1 || limit > max || count > limit || count !== Math.min(limit, Math.max(0, total - offset)))
    throw new DecodeError(`${path}: inconsistent bounded page`);
}
function requireBankGeneration(stored: string, current: string | null, state: string, path: string): void {
  const expected = current === null ? 'UNAVAILABLE' : stored === current ? 'CURRENT_GENERATION' : 'STALE_GENERATION';
  if (state !== expected) throw new DecodeError(`${path}: inconsistent generation label`);
}
export const decodeBankReconciliationPage = (raw: unknown, path: string) => {
  const page = bankPage(raw, path);
  requireBankPage(page.offset, page.limit, page.total, page.rows.length, 25, path);
  for (const row of page.rows) requireBankGeneration(row.inputGeneration, page.currentInputGeneration, row.generationState, path);
  if (new Set(page.rows.map(row => row.id)).size !== page.rows.length) throw new DecodeError(`${path}: duplicate record`);
  return page;
};
export const decodeBankReconciliationDetail = (raw: unknown, path: string) => {
  const detail = bankDetail(raw, path);
  requireBankPage(detail.offset, detail.limit, detail.total, detail.items.length, 100, path);
  requireBankGeneration(detail.reconciliation.inputGeneration, detail.currentInputGeneration, detail.reconciliation.generationState, path);
  for (const [schedule, id] of [[detail.ledgerSchedule, detail.ledgerScheduleId], [detail.statementSchedule, detail.statementScheduleId]] as const) {
    if (!schedule) continue;
    if (schedule.id !== id) throw new DecodeError(`${path}: mismatched source schedule`);
    requireBankGeneration(schedule.inputGeneration, detail.currentInputGeneration, schedule.generationState, path);
  }
  if (new Set(detail.items.map(item => item.id)).size !== detail.items.length) throw new DecodeError(`${path}: duplicate item`);
  return detail;
};
