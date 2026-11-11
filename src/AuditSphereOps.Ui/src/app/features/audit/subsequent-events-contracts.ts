import { arr, bool, guid, instant, obj, oneOf, str, text, DecodeError, type Decoder } from '../../core/decode';

// Unlike optional inputs, retained nullable output fields must be explicitly present.
const nil = <T>(decoder: Decoder<T>): Decoder<T | null> => (value, path) => value === null ? null : decoder(value, path);
const time: Decoder<string> = (v, p) => {
  const s = instant(v, p), date = s.slice(0, 10);
  if (!Number.isFinite(Date.parse(s)) || new Date(date + 'T00:00:00Z').toISOString().slice(0, 10) !== date)
    throw new DecodeError(`${p}: invalid timestamp`);
  return s;
};
export const subsequentEventRecord = obj({ id: guid, clientId: guid, engagementId: guid, procedureId: nil(guid),
  periodEndDate: text, eventDate: text, description: str(4000), classification: oneOf('ADJUSTING', 'NON_ADJUSTING', 'PENDING_ASSESSMENT'),
  adjustmentRequired: bool, disclosureRequired: bool, disclosureReference: nil(str(200)), rationale: nil(str(4000)), currency: str(3),
  financialEffect: nil(str(21)), evidenceReferences: arr(text, 5000), revision: text, recordedByUserId: guid,
  reviewedByUserId: nil(guid), recordedAt: time, reviewedAt: nil(time), reviewedRevision: nil(text) });
export const subsequentEventState = obj({ engagementId: guid, clientId: guid, selectedRecordId: nil(guid), expectedRevision: text,
  reviewBasis: text, record: nil(subsequentEventRecord), canPrepare: bool, canReview: bool, blockers: arr(text, 100) });
export const subsequentEventList = obj({ records: arr(subsequentEventRecord, 100), hasMore: bool });
export const subsequentEventPreview = obj({ requestId: guid, action: oneOf('PREPARE', 'REVIEW'), requestHash: text,
  reviewBasis: text, expectedRevision: text, canProceed: bool, blockers: arr(text, 100), currentRecord: nil(subsequentEventRecord), summary: text });
export const subsequentEventReceipt = obj({ id: guid, engagementId: guid, subsequentEventReviewId: guid, actorId: guid,
  actorEpoch: text, requestId: guid, requestHash: text, action: oneOf('PREPARE', 'REVIEW'), createdAt: time,
  preview: subsequentEventPreview, result: subsequentEventRecord });
export const subsequentEventLookup = obj({ found: bool, receipt: nil(subsequentEventReceipt) });
