import { arr, bool, dec, guid, instant, int, nat, nullable, obj, oneOf, text } from '../../core/decode';

const scopeStatus = oneOf('DRAFT', 'APPROVED');
const componentStatus = oneOf('SUBMITTED', 'APPROVED');
const journalStatus = oneOf('DRAFT', 'SUBMITTED', 'APPROVED', 'Returned');
const runStatus = oneOf('SUBMITTED', 'APPROVED');
const reportState = oneOf('ADVANCED_PROFILE', 'NO_RUN', 'REVIEW_REQUIRED', 'STALE', 'CURRENT_APPROVED');
const componentReadinessState = oneOf('NONE', 'DRAFT', 'SUBMITTED', 'APPROVED');
const intercompanyStatus = oneOf('DRAFT', 'SUBMITTED', 'APPROVED');

export const decodeScopeSummary = obj({
  id: guid,
  groupId: guid,
  groupName: text,
  groupCode: text,
  version: int,
  groupRevision: nat,
  periodId: guid,
  reportingCurrency: text,
  method: text,
  status: scopeStatus,
  openingBasis: text,
  isAdvanced: bool,
});

export const decodeConsolidationComponent = obj({
  id: guid,
  clientId: guid,
  clientLegalName: text,
  sourceType: text,
  packageId: nullable(guid),
  externalComponentPackId: nullable(guid),
  packageHash: text,
  periodBasis: text,
  taxonomyVersion: text,
  mappingVersion: text,
  currency: text,
  ownershipPercent: dec,
  controlMethod: text,
  status: componentStatus,
  submittedByUserId: guid,
  submittedAt: instant,
  approvedByUserId: nullable(guid),
  approvedAt: nullable(instant),
});

export const decodeEligiblePackage = obj({
  packageId: guid,
  clientId: guid,
  clientLegalName: text,
  engagementId: guid,
  periodBasis: text,
  taxonomyVersion: text,
  mappingVersion: text,
  currency: text,
  calculationHash: text,
});

export const decodeJournalLine = obj({
  id: guid,
  taxonomyCode: text,
  debit: dec,
  credit: dec,
  description: text,
  intercompanyMatchId: nullable(guid),
});

export const decodeConsolidationJournal = obj({
  id: guid,
  journalNumber: text,
  journalType: text,
  currency: text,
  totalDebits: dec,
  totalCreditsAbs: dec,
  evidenceReference: text,
  status: journalStatus,
  returnReason: nullable(text),
  createdByUserId: guid,
  createdAt: instant,
  approvedByUserId: nullable(guid),
  approvedAt: nullable(instant),
  lines: arr(decodeJournalLine, 500),
});

export const decodeConsolidationRun = obj({
  id: guid,
  engineVersion: text,
  runHash: text,
  signedTotal: dec,
  status: runStatus,
  createdByUserId: guid,
  createdAt: instant,
  approvedByUserId: nullable(guid),
  approvedAt: nullable(instant),
});

export const decodeConsolidationReportLine = obj({
  component: text,
  taxonomyCode: text,
  componentAmount: dec,
  alignmentAmount: dec,
  eliminationAmount: dec,
  consolidatedAmount: dec,
  currency: text,
});

export const decodeConsolidationReport = obj({
  scopeVersionId: guid,
  runId: nullable(guid),
  state: reportState,
  approvedAt: nullable(instant),
  lines: arr(decodeConsolidationReportLine, 20000),
});

export const decodeComponentReadiness = obj({
  clientId: guid,
  memberName: text,
  componentState: componentReadinessState,
  componentId: nullable(guid),
  currency: text,
  periodBasis: text,
  taxonomyVersion: text,
  mappingVersion: text,
  currencyCompatible: bool,
  mismatchReason: nullable(text),
});

export const decodeIntercompanyException = obj({
  matchId: guid,
  sellerClientId: guid,
  buyerClientId: guid,
  accountNature: text,
  matchMode: text,
  periodCode: text,
  currency: text,
  transactionReference: text,
  sellerAmount: dec,
  buyerAmount: dec,
  matchedAmount: dec,
  difference: dec,
  differenceReason: text,
  status: intercompanyStatus,
  outsidePerimeterReview: bool,
  evidenceReference: text,
});

export const decodeScopeWorkspace = obj({
  scope: decodeScopeSummary,
  canPrepare: bool,
  canReview: bool,
  currentUserId: guid,
  components: arr(decodeConsolidationComponent, 500),
  eligiblePackages: arr(decodeEligiblePackage, 500),
  journals: arr(decodeConsolidationJournal, 500),
  runs: arr(decodeConsolidationRun, 500),
  latestReport: nullable(decodeConsolidationReport),
  componentReadiness: arr(decodeComponentReadiness, 500),
  intercompanyExceptions: arr(decodeIntercompanyException, 500),
});

export type ScopeWorkspace = ReturnType<typeof decodeScopeWorkspace>;
export type ScopeSummary = ReturnType<typeof decodeScopeSummary>;
export type ConsolidationComponent = ReturnType<typeof decodeConsolidationComponent>;
export type EligiblePackage = ReturnType<typeof decodeEligiblePackage>;
export type ConsolidationJournal = ReturnType<typeof decodeConsolidationJournal>;
export type ConsolidationRun = ReturnType<typeof decodeConsolidationRun>;
export type ConsolidationReport = ReturnType<typeof decodeConsolidationReport>;
export type ComponentReadiness = ReturnType<typeof decodeComponentReadiness>;
export type IntercompanyException = ReturnType<typeof decodeIntercompanyException>;
