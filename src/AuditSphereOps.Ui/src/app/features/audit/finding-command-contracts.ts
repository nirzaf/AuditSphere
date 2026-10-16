import { Decoder, arr, bool, dec, guid, instant, nullable, obj, oneOf, sha256, str } from '../../core/decode';

export const findingAction = oneOf('RECORD_RESPONSE', 'DESIGNATE_MANAGEMENT_LETTER', 'UNDESIGNATE_MANAGEMENT_LETTER');
export type FindingAction = ReturnType<typeof findingAction>;
export const findingRevision: Decoder<string> = (v, p) => {
  if (typeof v !== 'string' || !/^(0|[1-9][0-9]{0,18})$/.test(v)) throw new Error(`${p}: expected exact revision or actor epoch`);
  return v;
};
const fieldsShape = obj({ action: findingAction,
  response: nullable(obj({ managementResponse: str(20000), corrected: bool })),
  designation: nullable(obj({ recommendation: str(2000) })) });
export const decodeFindingFields: Decoder<ReturnType<typeof fieldsShape>> = (v, p) => {
  const f = fieldsShape(v, p);
  if (f.action === 'RECORD_RESPONSE' ? !f.response?.managementResponse.trim() || !!f.designation :
    f.action === 'DESIGNATE_MANAGEMENT_LETTER' ? !f.designation?.recommendation.trim() || !!f.response : !!f.response || !!f.designation) throw new Error(`${p}: wrong finding payload`);
  return f;
};
export const decodeFindingSnapshot = obj({ findingId: guid, clientId: guid, engagementId: guid, revision: findingRevision, createdByUserId: guid,
  findingType: str(2000), impactDescription: str(20000), monetaryAmount: nullable(dec), corrected: bool, managementResponse: nullable(str(20000)), status: str(100),
  letterDesignatedAt: nullable(instant), letterDesignatedByUserId: nullable(guid), letterRecommendation: nullable(str(2000)), createdAt: instant });
export type FindingSnapshot = ReturnType<typeof decodeFindingSnapshot>;
export const decodeFindingCommandState = obj({ finding: decodeFindingSnapshot, reviewBasis: sha256, canRecordResponse: bool, canDesignate: bool,
  responseBlockers: arr(str(4000), 100), designationBlockers: arr(str(4000), 100) });
const previewShape = obj({ requestId: guid, requestHash: sha256, expectedRevision: findingRevision, reviewBasis: sha256, canonicalInput: decodeFindingFields,
  before: decodeFindingSnapshot, nextRevision: findingRevision, canProceed: bool, blockers: arr(str(4000), 100), summary: str(20000) });
export const decodeFindingPreview: Decoder<ReturnType<typeof previewShape>> = (v, p) => {
  const r = previewShape(v, p);
  if (r.before.revision !== r.expectedRevision || BigInt(r.nextRevision) !== BigInt(r.expectedRevision) + 1n || r.canProceed === (r.blockers.length > 0)) throw new Error(`${p}: inconsistent finding preview`);
  return r;
};
const amendment = obj({ id: guid, actorId: guid, actorEpoch: findingRevision, action: findingAction, createdAt: instant, before: decodeFindingSnapshot, after: decodeFindingSnapshot });
const receiptShape = obj({ id: guid, findingId: guid, actorId: guid, actorEpoch: findingRevision, requestId: guid, requestHash: sha256, reviewBasis: sha256,
  action: findingAction, createdAt: instant, preview: decodeFindingPreview, amendment });
export const decodeFindingReceipt: Decoder<ReturnType<typeof receiptShape>> = (v, p) => {
  const r = receiptShape(v, p), preview = r.preview, a = r.amendment, before = a.before, after = a.after, f = preview.canonicalInput;
  if (r.findingId !== preview.before.findingId || r.requestId !== preview.requestId || r.requestHash !== preview.requestHash || r.reviewBasis !== preview.reviewBasis ||
    r.action !== f.action || a.action !== r.action || a.actorId !== r.actorId || a.actorEpoch !== r.actorEpoch || !preview.canProceed ||
    JSON.stringify(before) !== JSON.stringify(preview.before) || after.revision !== preview.nextRevision ||
    ['findingId', 'clientId', 'engagementId', 'createdByUserId', 'findingType', 'impactDescription', 'monetaryAmount', 'createdAt'].some(k => before[k as keyof FindingSnapshot] !== after[k as keyof FindingSnapshot])) throw new Error(`${p}: receipt identity mismatch`);
  if (f.response ? after.managementResponse !== f.response.managementResponse || after.corrected !== f.response.corrected || after.status !== (f.response.corrected ? 'CORRECTED' : 'EVALUATED') ||
      after.letterDesignatedAt !== before.letterDesignatedAt || after.letterDesignatedByUserId !== before.letterDesignatedByUserId || after.letterRecommendation !== before.letterRecommendation :
    after.managementResponse !== before.managementResponse || after.corrected !== before.corrected || after.status !== before.status ||
      (f.designation ? after.letterRecommendation !== f.designation.recommendation || !after.letterDesignatedAt || after.letterDesignatedByUserId !== r.actorId :
        after.letterDesignatedAt !== null || after.letterDesignatedByUserId !== null || after.letterRecommendation !== null)) throw new Error(`${p}: amendment differs from reviewed intent`);
  return r;
};
export const decodeFindingLookup = obj({ found: bool, receipt: nullable(decodeFindingReceipt) });
export type FindingFields = ReturnType<typeof decodeFindingFields>;
export type FindingPreview = ReturnType<typeof decodeFindingPreview>;
export type FindingReceipt = ReturnType<typeof decodeFindingReceipt>;
export const findingActions: readonly FindingAction[] = ['RECORD_RESPONSE', 'DESIGNATE_MANAGEMENT_LETTER', 'UNDESIGNATE_MANAGEMENT_LETTER'];
export function sameFindingFields(a: FindingFields, b: FindingFields): boolean { return JSON.stringify(a) === JSON.stringify(b); }
