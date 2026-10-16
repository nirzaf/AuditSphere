import { Decoder, arr, bool, guid, instant, nullable, obj, oneOf, sha256, str } from '../../core/decode';

export const reviewPointAction = oneOf('CLEAR', 'REOPEN');
export type ReviewPointAction = ReturnType<typeof reviewPointAction>;
export const reviewPointRevision: Decoder<string> = (value, path) => {
  if (typeof value !== 'string' || !/^(0|[1-9][0-9]{0,18})$/.test(value) || BigInt(value) > 9223372036854775807n) throw new Error(`${path}: expected exact revision or actor epoch`);
  return value;
};
export const reviewPointTargetRevision: Decoder<string> = (value, path) =>
  reviewPointRevision(typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? String(value) : value, path);
const fieldsShape = obj({ action: reviewPointAction });
export const decodeReviewPointFields: Decoder<ReturnType<typeof fieldsShape>> = (value, path) => {
  const fields = fieldsShape(value, path);
  if (Object.keys(value as object).length !== 1) throw new Error(`${path}: expected only the exact disposition action`);
  return fields;
};
const snapshotShape = obj({ reviewPointId: guid, clientId: guid, engagementId: guid, dispositionRevision: reviewPointRevision,
  targetKind: str(100), targetId: guid, targetRevision: reviewPointRevision, raisedByUserId: guid, raisedAt: instant,
  significant: bool, cleared: bool, comment: str(20000), status: oneOf('CLEARED', 'BLOCKING', 'OPEN') });
export const reviewPointStatus = (cleared: boolean, significant: boolean) => cleared ? 'CLEARED' : significant ? 'BLOCKING' : 'OPEN';
export const decodeReviewPointSnapshot: Decoder<ReturnType<typeof snapshotShape>> = (value, path) => {
  const s = snapshotShape(value, path);
  if (s.dispositionRevision === '0' || s.status !== reviewPointStatus(s.cleared, s.significant)) throw new Error(`${path}: inconsistent review point snapshot`);
  return s;
};
export type ReviewPointSnapshot = ReturnType<typeof decodeReviewPointSnapshot>;
// Compare offsets as exact instants while retaining sub-millisecond evidence, never rounding it through Date.
function instantIdentity(value: string): string {
  const match = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,9}))?(Z|[+-]\d{2}:\d{2})$/.exec(value);
  if (!match) throw new Error('Unsupported review point timestamp precision');
  const whole = Date.parse(match[1] + match[3]);
  if (!Number.isSafeInteger(whole)) throw new Error('Invalid review point timestamp');
  return (BigInt(whole) * 1000000n + BigInt((match[2] ?? '').padEnd(9, '0'))).toString();
}
export function sameReviewPointSnapshot(a: ReviewPointSnapshot, b: ReviewPointSnapshot): boolean {
  return JSON.stringify({ ...a, raisedAt: instantIdentity(a.raisedAt) }) === JSON.stringify({ ...b, raisedAt: instantIdentity(b.raisedAt) });
}
export const decodeReviewPointCommandState = obj({ reviewPoint: decodeReviewPointSnapshot, reviewBasis: sha256, canChangeDisposition: bool, blockers: arr(str(4000), 100) });
const previewShape = obj({ requestId: guid, requestHash: sha256, expectedDispositionRevision: reviewPointRevision, reviewBasis: sha256,
  canonicalInput: decodeReviewPointFields, before: decodeReviewPointSnapshot, nextDispositionRevision: reviewPointRevision,
  cleared: bool, canProceed: bool, blockers: arr(str(4000), 100), summary: str(20000) });
export const decodeReviewPointPreview: Decoder<ReturnType<typeof previewShape>> = (value, path) => {
  const p = previewShape(value, path);
  if (p.expectedDispositionRevision !== p.before.dispositionRevision || BigInt(p.nextDispositionRevision) !== BigInt(p.expectedDispositionRevision) + 1n ||
    p.cleared !== (p.canonicalInput.action === 'CLEAR') || p.canProceed === (p.blockers.length > 0)) throw new Error(`${path}: inconsistent disposition preview`);
  return p;
};
const event = obj({ id: guid, actorId: guid, actorEpoch: reviewPointRevision, action: reviewPointAction, createdAt: instant,
  before: decodeReviewPointSnapshot, after: decodeReviewPointSnapshot });
const receiptShape = obj({ id: guid, reviewPointId: guid, actorId: guid, actorEpoch: reviewPointRevision, requestId: guid, requestHash: sha256,
  reviewBasis: sha256, action: reviewPointAction, createdAt: instant, preview: decodeReviewPointPreview, dispositionEvent: event });
export const decodeReviewPointReceipt: Decoder<ReturnType<typeof receiptShape>> = (value, path) => {
  const r = receiptShape(value, path), p = r.preview, e = r.dispositionEvent;
  if (r.reviewPointId !== p.before.reviewPointId || r.requestId !== p.requestId || r.requestHash !== p.requestHash || r.reviewBasis !== p.reviewBasis ||
    r.action !== p.canonicalInput.action || !p.canProceed || e.action !== r.action || e.actorId !== r.actorId || e.actorEpoch !== r.actorEpoch ||
    !sameReviewPointSnapshot(e.before, p.before) || !sameReviewPointSnapshot(e.after, { ...e.before, dispositionRevision: p.nextDispositionRevision,
      cleared: p.cleared, status: reviewPointStatus(p.cleared, e.before.significant) })) throw new Error(`${path}: disposition receipt differs from reviewed scope, target or effect`);
  return r;
};
export const decodeReviewPointLookup = obj({ found: bool, receipt: nullable(decodeReviewPointReceipt) });
export type ReviewPointFields = ReturnType<typeof decodeReviewPointFields>;
export type ReviewPointPreview = ReturnType<typeof decodeReviewPointPreview>;
export type ReviewPointReceipt = ReturnType<typeof decodeReviewPointReceipt>;
export const reviewPointActions: readonly ReviewPointAction[] = ['CLEAR', 'REOPEN'];
export const sameReviewPointFields = (a: ReviewPointFields, b: ReviewPointFields) => a.action === b.action;
