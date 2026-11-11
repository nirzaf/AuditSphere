import { arr, bool, guid, instant, nullable, obj, oneOf, text } from '../../core/decode';

/** Exact server identities stay strings; never round a record revision through Number. */
export const goingConcernRecord = obj({ id: guid, clientId: guid, engagementId: guid, procedureId: nullable(guid),
  assessmentDate: text, periodCoveredTo: text, forecastReviewOutcome: text, materialUncertaintyIdentified: bool,
  disclosureAdequate: bool, conclusion: text, currency: text, rationale: text, evidenceReferences: arr(text, 100),
  revision: text, recordedByUserId: guid, reviewedByUserId: nullable(guid), recordedAt: instant,
  reviewedAt: nullable(instant), reviewedRevision: nullable(text) });
export const goingConcernState = obj({ engagementId: guid, clientId: guid, assessmentDate: text,
  expectedRevision: text, reviewBasis: text, record: nullable(goingConcernRecord), canPrepare: bool,
  canReview: bool, blockers: arr(text, 100) });
export const goingConcernPreview = obj({ requestId: guid, action: oneOf('PREPARE', 'REVIEW'), requestHash: text,
  reviewBasis: text, expectedRevision: text, canProceed: bool, blockers: arr(text, 100),
  currentRecord: nullable(goingConcernRecord), summary: text });
export const goingConcernReceipt = obj({ id: guid, engagementId: guid, goingConcernAssessmentId: guid,
  actorId: guid, actorEpoch: text, requestId: guid, requestHash: text, action: oneOf('PREPARE', 'REVIEW'),
  createdAt: instant, preview: goingConcernPreview, result: goingConcernRecord });
export const goingConcernLookup = obj({ found: bool, receipt: nullable(goingConcernReceipt) });
