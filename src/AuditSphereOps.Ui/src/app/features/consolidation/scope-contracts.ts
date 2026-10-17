import { arr, bool, date, dec, guid, instant, int, nat, nullable, obj, oneOf, text, type Decoder } from '../../core/decode';

function pageOf<T>(item: Decoder<T>): Decoder<{ items: T[]; totalCount: number; page: number; pageSize: number }> {
  const decode = obj({ items: arr(item, 100), totalCount: int, page: int, pageSize: int });
  return (value, path) => {
    const page = decode(value, path);
    if (page.totalCount < 0 || page.page < 1 || page.pageSize < 1 || page.pageSize > 100 || page.items.length > page.pageSize)
      throw new Error(`${path || 'response'}: invalid bounded history page`);
    return page;
  };
}

function readResultOf<T>(valueDecoder: Decoder<T>): Decoder<{
  available: boolean; value: T | null; errorCode: string | null; message: string | null;
}> {
  const decode = obj({ available: bool, value: nullable(valueDecoder), errorCode: nullable(text), message: nullable(text) });
  return (value, path) => {
    const result = decode(value, path);
    const validSuccess = result.available && result.value !== null && result.errorCode === null && result.message === null;
    const validRefusal = !result.available && result.value === null && Boolean(result.errorCode) && Boolean(result.message);
    if (!validSuccess && !validRefusal) throw new Error(`${path || 'response'}: invalid scoped read result`);
    return result;
  };
}

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
  unmatchedResidual: dec,
  differenceReason: text,
  status: intercompanyStatus,
  outsidePerimeterReview: bool,
  evidenceReference: text,
  sellerTaxonomyCode: text,
  buyerTaxonomyCode: text,
  matchGroupReference: text,
  createdByUserId: guid,
  createdAt: instant,
  reviewedByUserId: nullable(guid),
  reviewedAt: nullable(instant),
});

const decodeIntercompanyExceptionsPageShape = obj({
  scopeVersionId: guid,
  groupId: guid,
  pinnedGroupRevision: nat,
  currentGroupRevision: nat,
  items: arr(decodeIntercompanyException, 500),
  totalCount: int,
  page: int,
  pageSize: int,
  totalUnmatchedDifference: dec,
  totalGrossUnmatchedExposure: dec,
});

export const decodeIntercompanyExceptionsPage = (value: unknown, path: string) => {
  const page = decodeIntercompanyExceptionsPageShape(value, path);
  if (page.totalCount < 0 || page.page < 1 || page.pageSize < 1 || page.pageSize > 500 || page.items.length > page.pageSize)
    throw new Error(`${path || 'response'}: invalid intercompany exception page`);
  return page;
};

const decodeIntercompanyMatchOptionsShape = obj({
  scopeVersionId: guid,
  groupId: guid,
  pinnedGroupRevision: nat,
  currentGroupRevision: nat,
  reportingCurrency: text,
  periodStartDate: nullable(date),
  periodEndDate: nullable(date),
  membershipBlocker: nullable(text),
  members: arr(obj({ clientId: guid, legalName: text, hasApprovedComponent: bool }), 500),
  membersTruncated: bool,
  taxonomyCodes: arr(text, 500),
  taxonomyCodesTruncated: bool,
});

export const decodeIntercompanyMatchOptions = (value: unknown, path: string) => {
  const options = decodeIntercompanyMatchOptionsShape(value, path);
  const periodMissing = options.periodStartDate === null && options.periodEndDate === null;
  const periodValid = options.periodStartDate !== null && options.periodEndDate !== null &&
    options.periodStartDate <= options.periodEndDate;
  if ((options.membershipBlocker !== null && (!periodMissing || options.members.length !== 0)) ||
      (options.membershipBlocker === null && !periodValid))
    throw new Error(`${path || 'response'}: invalid period-bound membership options`);
  return options;
};

const decodePaging = obj({ totalCount: nat, page: int, pageSize: int });

const decodeScopeWorkspaceShape = obj({
  scope: decodeScopeSummary,
  canPrepare: bool,
  canReview: bool,
  currentUserId: guid,
  components: arr(decodeConsolidationComponent, 100),
  componentPaging: decodePaging,
  resolvedComponent: nullable(decodeConsolidationComponent),
  eligiblePackages: pageOf(decodeEligiblePackage),
  journals: pageOf(decodeConsolidationJournal),
  runs: pageOf(decodeConsolidationRun),
  latestReport: readResultOf(decodeConsolidationReport),
  componentReadiness: readResultOf(arr(decodeComponentReadiness, 100)),
  readinessPaging: nullable(decodePaging),
  intercompanyExceptions: readResultOf(decodeIntercompanyExceptionsPage),
});

export const decodeScopeWorkspace = (value: unknown, path: string) => {
  const workspace = decodeScopeWorkspaceShape(value, path);
  const validate = (paging: ReturnType<typeof decodePaging>, count: number) => {
    const remaining = Math.max(0, paging.totalCount - (paging.page - 1) * paging.pageSize);
    if (paging.page < 1 || paging.pageSize < 1 || paging.pageSize > 100 ||
        count !== Math.min(paging.pageSize, remaining))
      throw new Error(`${path || 'response'}: inconsistent bounded component page`);
  };
  validate(workspace.componentPaging, workspace.components.length);
  if (workspace.componentReadiness.available) {
    if (!workspace.readinessPaging) throw new Error(`${path || 'response'}: missing readiness paging`);
    validate(workspace.readinessPaging, workspace.componentReadiness.value!.length);
  } else if (workspace.readinessPaging !== null) {
    throw new Error(`${path || 'response'}: refused readiness cannot have paging`);
  }
  return workspace;
};

export type ScopeWorkspace = ReturnType<typeof decodeScopeWorkspace>;
export type ScopeSummary = ReturnType<typeof decodeScopeSummary>;
export type ConsolidationComponent = ReturnType<typeof decodeConsolidationComponent>;
export type EligiblePackage = ReturnType<typeof decodeEligiblePackage>;
export type ConsolidationJournal = ReturnType<typeof decodeConsolidationJournal>;
export type ConsolidationRun = ReturnType<typeof decodeConsolidationRun>;
export type ConsolidationReport = ReturnType<typeof decodeConsolidationReport>;
export type ComponentReadiness = ReturnType<typeof decodeComponentReadiness>;
export type IntercompanyException = ReturnType<typeof decodeIntercompanyException>;
export type IntercompanyExceptionsPage = ReturnType<typeof decodeIntercompanyExceptionsPage>;
export type IntercompanyMatchOptions = ReturnType<typeof decodeIntercompanyMatchOptions>;
