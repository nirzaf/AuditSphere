import { bool, decode, guid, instant, nullable, obj, str } from '../../core/decode';

export type AssessmentCommandKind =
  'ANSWER' | 'REQUEST_REVIEW' | 'RECORD_REVIEW' | 'DECISION' | 'CONTINUANCE';
export interface AssessmentCommandFields {
  kind: AssessmentCommandKind;
  generation: string;
  revision: string | null;
  questionCode: string | null;
  answer: string | null;
  evidence: string | null;
  area: string | null;
  specialist: string | null;
  reviewId: string | null;
  status: string | null;
  expectedStatus: string | null;
  conditions: string | null;
  serviceRoute: string | null;
  decision: string | null;
  rationale: string | null;
}
export const assessmentFields = (
  kind: AssessmentCommandKind,
  generation: string,
): AssessmentCommandFields => ({
  kind,
  generation,
  revision: null,
  questionCode: null,
  answer: null,
  evidence: null,
  area: null,
  specialist: null,
  reviewId: null,
  status: null,
  expectedStatus: null,
  conditions: null,
  serviceRoute: null,
  decision: null,
  rationale: null,
});
const maximum = 9223372036854775807n;
const counter = (v: string, min = 1n) =>
  /^(0|[1-9]\d{0,18})$/.test(v) && BigInt(v) >= min && BigInt(v) <= maximum;
const hash = (v: string) => /^[a-f0-9]{64}$/.test(v);
const nonzero = (v: string) => v !== '00000000-0000-0000-0000-000000000000';
const text = (v: string | null) =>
  v === null || (v === v.trim() && !/[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]/.test(v));
const fieldDecoder = obj({
  kind: str(20),
  generation: str(19),
  revision: nullable(str(19)),
  questionCode: nullable(str(50)),
  answer: nullable(str(2000)),
  evidence: nullable(str(500)),
  area: nullable(str(100)),
  specialist: nullable(str(200)),
  reviewId: nullable(guid),
  status: nullable(str(20)),
  expectedStatus: nullable(str(20)),
  conditions: nullable(str(2000)),
  serviceRoute: nullable(str(100)),
  decision: nullable(str(40)),
  rationale: nullable(str(2000)),
});
/** Canonical, bounded action shape. Irrelevant fields cannot silently change the reviewed action. */
export function decodeAssessmentFields(raw: unknown): AssessmentCommandFields {
  const f = decode(fieldDecoder, raw);
  if (!counter(f.generation) || Object.values(f).some((v) => !text(v)))
    throw new Error('Invalid assessment fields');
  const shaped = assessmentFields(f.kind as AssessmentCommandKind, f.generation);
  switch (f.kind) {
    case 'ANSWER':
      if (
        !f.questionCode ||
        f.questionCode !== f.questionCode.toUpperCase() ||
        !f.answer ||
        f.revision === null ||
        !counter(f.revision, 0n)
      )
        throw new Error('Invalid answer');
      Object.assign(shaped, {
        questionCode: f.questionCode,
        answer: f.answer,
        evidence: f.evidence,
        revision: f.revision,
      });
      break;
    case 'REQUEST_REVIEW':
      if (!f.area || f.area.length < 2 || !f.specialist || f.specialist.length < 2)
        throw new Error('Invalid review request');
      Object.assign(shaped, { area: f.area, specialist: f.specialist });
      break;
    case 'RECORD_REVIEW':
      if (
        !f.reviewId ||
        !nonzero(f.reviewId) ||
        !['CLEARED', 'HOLD', 'CONDITIONS'].includes(f.status ?? '') ||
        !['PENDING', 'HOLD', 'CONDITIONS'].includes(f.expectedStatus ?? '') ||
        (f.status === 'CLEARED' && !f.evidence) ||
        (f.status === 'CONDITIONS' && !f.conditions)
      )
        throw new Error('Invalid review result');
      Object.assign(shaped, {
        reviewId: f.reviewId,
        status: f.status,
        expectedStatus: f.expectedStatus,
        evidence: f.evidence,
        conditions: f.conditions,
      });
      break;
    case 'DECISION':
      if (
        !f.serviceRoute ||
        !f.rationale ||
        !['Accepted', 'AcceptedWithConditions', 'Declined', 'Deferred'].includes(
          f.decision ?? '',
        ) ||
        (f.decision === 'Accepted' && f.conditions !== null) ||
        (f.decision === 'AcceptedWithConditions' && !f.conditions)
      )
        throw new Error('Invalid decision');
      Object.assign(shaped, {
        serviceRoute: f.serviceRoute,
        decision: f.decision,
        rationale: f.rationale,
        conditions: f.conditions,
      });
      break;
    case 'CONTINUANCE':
      break;
    default:
      throw new Error('Unsupported assessment action');
  }
  if (JSON.stringify(shaped) !== JSON.stringify(f)) throw new Error('Irrelevant assessment fields');
  return shaped;
}
export const sameAssessmentFields = (a: AssessmentCommandFields, b: AssessmentCommandFields) =>
  JSON.stringify(a) === JSON.stringify(b);
const beforeDecoder = obj({
  generation: str(19),
  path: str(40),
  decision: nullable(str(40)),
  ready: bool,
  answer: nullable(str(2000)),
  evidence: nullable(str(500)),
  revision: nullable(str(19)),
  reviewStatus: nullable(str(20)),
  reviewConditions: nullable(str(2000)),
});
const previewDecoder = obj({
  clientId: guid,
  requestId: guid,
  requestHash: str(64),
  reviewBasis: str(64),
  fields: decodeAssessmentFields,
  before: beforeDecoder,
  effect: str(1000),
});
export function decodeAssessmentPreview(raw: unknown) {
  const p = decode(previewDecoder, raw);
  if (
    !nonzero(p.clientId) ||
    !nonzero(p.requestId) ||
    !hash(p.requestHash) ||
    !hash(p.reviewBasis) ||
    p.before.generation !== p.fields.generation ||
    !['NEW_CLIENT', 'CONTINUANCE'].includes(p.before.path) ||
    (p.before.decision !== null &&
      !['Pending', 'Accepted', 'AcceptedWithConditions', 'Declined', 'Deferred'].includes(
        p.before.decision,
      )) ||
    (p.before.revision !== null && !counter(p.before.revision, 0n)) ||
    (p.fields.kind === 'ANSWER' && p.before.revision !== p.fields.revision) ||
    (p.fields.kind === 'RECORD_REVIEW' && p.before.reviewStatus !== p.fields.expectedStatus) ||
    (p.fields.kind === 'DECISION' &&
      ['Accepted', 'AcceptedWithConditions'].includes(p.fields.decision ?? '') &&
      !p.before.ready) ||
    !p.effect.trim()
  )
    throw new Error('Invalid assessment preview');
  return p;
}
export type AssessmentPreview = ReturnType<typeof decodeAssessmentPreview>;
const receiptDecoder = obj({
  id: guid,
  clientId: guid,
  actorId: guid,
  requestId: guid,
  requestHash: str(64),
  reviewBasis: str(64),
  kind: str(20),
  generation: str(19),
  resultGeneration: str(19),
  resourceId: guid,
  preview: decodeAssessmentPreview,
  createdAt: instant,
});
export function decodeAssessmentReceipt(raw: unknown) {
  const r = decode(receiptDecoder, raw),
    p = r.preview;
  if (
    ![r.id, r.clientId, r.actorId, r.requestId, r.resourceId].every(nonzero) ||
    !counter(r.generation) ||
    !counter(r.resultGeneration) ||
    r.clientId !== p.clientId ||
    r.requestId !== p.requestId ||
    r.requestHash !== p.requestHash ||
    r.reviewBasis !== p.reviewBasis ||
    r.kind !== p.fields.kind ||
    r.generation !== p.fields.generation ||
    BigInt(r.resultGeneration) !== BigInt(r.generation) + (r.kind === 'CONTINUANCE' ? 1n : 0n)
  )
    throw new Error('Invalid assessment receipt');
  return r;
}
export type AssessmentReceipt = ReturnType<typeof decodeAssessmentReceipt>;
const lookupDecoder = obj({ found: bool, receipt: nullable(decodeAssessmentReceipt) });
export function decodeAssessmentLookup(raw: unknown) {
  const v = decode(lookupDecoder, raw);
  if (v.found !== !!v.receipt) throw new Error('Invalid receipt lookup');
  return v;
}
