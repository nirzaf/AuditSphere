import { arr, bool, dec, guid, instant, nullable, obj, oneOf, text } from '../../core/decode';

/** Exact server identities stay strings; never round a record revision through Number. */
export const openingBalanceRecord = obj({ id: guid, engagementId: guid, procedureId: nullable(guid), priorPeriodId: nullable(guid),
  priorReference: text, asOfDate: text, currency: text, openingSignedTotal: dec, agreedSignedTotal: dec, differenceAmount: dec,
  accountingPoliciesConsistent: bool, conclusion: oneOf('AGREED', 'DIFFERENCES_RESOLVED', 'DIFFERENCES_UNRESOLVED', 'NOT_VERIFIABLE'),
  rationale: text, evidenceReferences: arr(text, 10000), revision: text, recordedByUserId: guid, recordedAt: instant,
  reviewedByUserId: nullable(guid), reviewedAt: nullable(instant), reviewedBasisRevision: nullable(text), reviewComment: nullable(text),
  comparisonResolved: bool, currentIndependentReview: bool });
export const openingBalanceState = obj({ engagementId: guid, clientId: guid, asOfDate: text,
  expectedRevision: text, reviewBasis: text, record: nullable(openingBalanceRecord), canPrepare: bool,
  canReview: bool, blockers: arr(text, 100) });
export const openingBalancePreview = obj({ requestId: guid, action: oneOf('PREPARE', 'REVIEW'), requestHash: text,
  reviewBasis: text, expectedRevision: text, canProceed: bool, blockers: arr(text, 100),
  currentRecord: nullable(openingBalanceRecord), summary: text });
export const openingBalanceReceipt = obj({ id: guid, engagementId: guid, verificationId: guid,
  actorId: guid, actorEpoch: text, requestId: guid, requestHash: text, action: oneOf('PREPARE', 'REVIEW'),
  createdAt: instant, preview: openingBalancePreview, result: openingBalanceRecord });
export const openingBalanceLookup = obj({ found: bool, receipt: nullable(openingBalanceReceipt) });
