import {
  arr,
  bool,
  decode,
  guid as decodeGuid,
  instant,
  nat,
  nullable,
  obj,
  str,
} from '../../core/decode';
export interface Question {
  code: string;
  section: string;
  prompt: string;
  category: string;
  answerType: string;
  requiresEvidence: boolean;
  adverse: boolean;
  answer: string | null;
  evidence: string | null;
  revision: string;
  priorAnswer: string | null;
  answeredBy: string | null;
}
export interface Clearance {
  id: string;
  area: string;
  specialist: string;
  status: string;
  evidence: string | null;
  conditions: string | null;
  createdAt: string;
  clearedAt: string | null;
}
export interface SpecialistEvent {
  id: string;
  reviewId: string;
  action: string;
  area: string;
  specialist: string;
  status: string | null;
  evidence: string | null;
  conditions: string | null;
  actor: string;
  occurredAt: string;
}
export interface Checklist {
  clientId: string;
  generation: string;
  path: string;
  currentDecision: string | null;
  priorDecision: string | null;
  ready: boolean;
  canEdit: boolean;
  canReview: boolean;
  canDecide: boolean;
  canStartContinuance: boolean;
  questions: Question[];
  clearances: Clearance[];
  blockers: { kind: string; message: string; questionCode: string | null }[];
}
const exactCounter = (v: unknown, minimum: bigint): v is string =>
  typeof v === 'string' &&
  /^(0|[1-9]\d{0,18})$/.test(v) &&
  BigInt(v) >= minimum &&
  BigInt(v) <= 9223372036854775807n;
const bounded = (v: unknown, max: number): v is string => typeof v === 'string' && v.length <= max;
const decisions = ['Pending', 'Accepted', 'AcceptedWithConditions', 'Declined', 'Deferred'];
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function decodeChecklist(value: unknown): Checklist {
  if (!value || typeof value !== 'object') throw new Error('Invalid checklist');
  const v = value as Record<string, unknown>;
  if (
    typeof v['clientId'] !== 'string' ||
    !guid.test(v['clientId']) ||
    typeof v['generation'] !== 'string' ||
    !exactCounter(v['generation'], 1n) ||
    !['NEW_CLIENT', 'CONTINUANCE'].includes(v['path'] as string) ||
    typeof v['ready'] !== 'boolean' ||
    !['canEdit', 'canReview', 'canDecide', 'canStartContinuance'].every(
      (k) => typeof v[k] === 'boolean',
    )
  )
    throw new Error('Invalid context');
  for (const key of ['questions', 'clearances', 'blockers'])
    if (!Array.isArray(v[key]) || v[key].length > 1000) throw new Error('Invalid checklist bounds');
  for (const q of v['questions'] as Record<string, unknown>[]) {
    if (
      !q ||
      !['code', 'section', 'prompt', 'category', 'answerType', 'revision'].every(
        (k) => typeof q[k] === 'string',
      ) ||
      !exactCounter(q['revision'], 0n) ||
      !['BOOLEAN', 'TEXT'].includes(q['answerType'] as string) ||
      !bounded(q['code'], 100) ||
      !bounded(q['section'], 300) ||
      !bounded(q['prompt'], 20000) ||
      !bounded(q['category'], 300) ||
      typeof q['requiresEvidence'] !== 'boolean' ||
      typeof q['adverse'] !== 'boolean' ||
      !['answer', 'evidence', 'priorAnswer', 'answeredBy'].every(
        (k) =>
          q[k] === null || bounded(q[k], k === 'evidence' ? 500 : k === 'answeredBy' ? 300 : 2000),
      )
    )
      throw new Error('Invalid question');
  }
  for (const b of v['blockers'] as Record<string, unknown>[])
    if (
      !b ||
      !bounded(b['kind'], 100) ||
      !bounded(b['message'], 20000) ||
      (b['questionCode'] !== null && !bounded(b['questionCode'], 100))
    )
      throw new Error('Invalid blocker');
  for (const c of v['clearances'] as Record<string, unknown>[])
    if (
      !c ||
      typeof c['id'] !== 'string' ||
      !guid.test(c['id']) ||
      !['area', 'specialist', 'status'].every((k) => typeof c[k] === 'string') ||
      !['PENDING', 'CLEARED', 'HOLD', 'CONDITIONS'].includes(c['status'] as string) ||
      !bounded(c['area'], 100) ||
      !bounded(c['specialist'], 200) ||
      !['evidence', 'conditions'].every(
        (k) => c[k] === null || bounded(c[k], k === 'evidence' ? 500 : 2000),
      ) ||
      typeof c['createdAt'] !== 'string' || !Number.isFinite(Date.parse(c['createdAt'])) ||
      (c['clearedAt'] !== null && (typeof c['clearedAt'] !== 'string' || !Number.isFinite(Date.parse(c['clearedAt']))))
    )
      throw new Error('Invalid clearance');
  for (const k of ['currentDecision', 'priorDecision'])
    if (v[k] !== null && !decisions.includes(v[k] as string)) throw new Error('Invalid decision');
  if (
    new Set((v['questions'] as Question[]).map((q) => q.code.toUpperCase())).size !==
      (v['questions'] as Question[]).length ||
    new Set((v['clearances'] as Clearance[]).map((c) => c.id.toLowerCase())).size !==
      (v['clearances'] as Clearance[]).length
  )
    throw new Error('Duplicate assessment identity');
  return v as unknown as Checklist;
}
const decodeAssessmentMetadata = obj({
  client: obj({
    id: decodeGuid,
    legalName: str(300),
    registrationNumber: nullable(str(200)),
    status: str(100),
  }),
  selectedDecision: nullable(
    obj({
      id: decodeGuid,
      engagementId: nullable(decodeGuid),
      generation: str(19),
      decision: str(40),
      serviceRoute: str(100),
      rationale: str(20000),
      conditions: nullable(str(20000)),
      path: str(40),
      priorDecisionId: nullable(decodeGuid),
      decidedBy: nullable(str(300)),
      decidedAt: nullable(instant),
    }),
  ),
  historical: bool,
  repository: nullable(
    obj({ logicalKey: str(500), state: str(100), lastVerifiedAt: nullable(instant) }),
  ),
  answered: nat,
  total: nat,
  clearedReviews: nat,
  totalReviews: nat,
  sections: arr(obj({ section: str(300), answered: nat, total: nat }), 1000),
  specialistTimeline: arr(obj({
    id: decodeGuid,
    reviewId: decodeGuid,
    action: str(40),
    area: str(100),
    specialist: str(200),
    status: nullable(str(20)),
    evidence: nullable(str(500)),
    conditions: nullable(str(2000)),
    actor: str(300),
    occurredAt: instant,
  }), 200),
});
export function decodeAssessment(value: unknown) {
  const metadata = decode(decodeAssessmentMetadata, value);
  const checklist = decodeChecklist((value as Record<string, unknown>)['checklist']);
  if (
    metadata.selectedDecision &&
    (!exactCounter(metadata.selectedDecision.generation, 1n) ||
      !decisions.includes(metadata.selectedDecision.decision))
  )
    throw new Error('Invalid recorded decision');
  if (
    metadata.client.id !== checklist.clientId ||
    metadata.answered > metadata.total ||
    metadata.clearedReviews > metadata.totalReviews ||
    metadata.totalReviews !== checklist.clearances.length ||
    metadata.specialistTimeline.some((event, i, events) =>
      event.reviewId === '00000000-0000-0000-0000-000000000000' ||
      !['Review requested', 'Result recorded'].includes(event.action) ||
      !['PENDING', 'CLEARED', 'HOLD', 'CONDITIONS'].includes(event.status ?? '') ||
      (i > 0 && Date.parse(events[i - 1].occurredAt) > Date.parse(event.occurredAt))) ||
    metadata.clearedReviews !== checklist.clearances.filter((c) => c.status === 'CLEARED').length ||
    metadata.answered !== checklist.questions.filter((q) => !!q.answer?.trim()).length ||
    new Set(metadata.sections.map((s) => s.section)).size !== metadata.sections.length ||
    metadata.total !== checklist.questions.length ||
    metadata.sections.some((s) => s.answered > s.total) ||
    metadata.sections.reduce((n, s) => n + s.total, 0) !== metadata.total ||
    metadata.sections.reduce((n, s) => n + s.answered, 0) !== metadata.answered ||
    metadata.historical !==
      !!(metadata.selectedDecision && metadata.selectedDecision.generation !== checklist.generation)
  )
    throw new Error('Invalid assessment context');
  if (
    (metadata.historical || metadata.selectedDecision?.engagementId) &&
    (checklist.canEdit ||
      checklist.canReview ||
      checklist.canDecide ||
      checklist.canStartContinuance)
  )
    throw new Error('Historical assessment is read only');
  return { ...metadata, checklist };
}
export type Assessment = ReturnType<typeof decodeAssessment>;
