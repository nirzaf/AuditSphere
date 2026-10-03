import {
  arr,
  bool,
  date,
  guid,
  instant,
  nat,
  nullable,
  obj,
  oneOf,
  sha256,
  str,
} from '../../core/decode';
const generation = (v: unknown): string => {
  if (typeof v !== 'string' || !/^[1-9]\d{0,18}$/.test(v) || BigInt(v) > 9223372036854775807n)
    throw new Error('Unsupported revision');
  return v;
};
const timestamp = (v: unknown, p: string) => {
  const s = instant(v, p);
  if (!Number.isFinite(Date.parse(s))) throw new Error('Unsupported time');
  return s;
};
const decision = obj({
  id: guid,
  decision: oneOf('Accepted', 'AcceptedWithConditions', 'Declined', 'Deferred'),
  path: oneOf('NEW_CLIENT', 'CONTINUANCE'),
  generation,
  decidedAt: nullable(timestamp),
});
const state = obj({
  engagementId: guid,
  clientId: guid,
  clientName: str(500),
  serviceRoute: str(50),
  serviceProfileId: str(100),
  periodStart: date,
  periodEnd: date,
  status: oneOf('Draft', 'Active', 'Completed', 'Closed'),
  engagementGeneration: generation,
  clientGeneration: generation,
  decision: nullable(decision),
  activeHolds: nat,
  eligible: bool,
  reviewBasis: sha256,
  blockers: arr(obj({ code: str(100), message: str(2000) }), 10),
});
export function decodeActivationState(raw: unknown, path = '') {
  const s = state(raw, path);
  if (
    s.eligible &&
    (s.status !== 'Draft' ||
      s.activeHolds !== 0 ||
      s.blockers.length !== 0 ||
      !s.decision ||
      s.decision.decision !== 'Accepted' ||
      s.decision.generation !== s.clientGeneration ||
      s.engagementGeneration === '9223372036854775807')
  )
    throw new Error('Unsupported eligibility');
  if (s.eligible !== (s.blockers.length === 0)) throw new Error('Unsupported blockers');
  return s;
}
export const decodeActivationPreview = obj({
  engagementId: guid,
  requestId: guid,
  reviewBasis: sha256,
  requestHash: sha256,
});
const receipt = obj({
  id: guid,
  engagementId: guid,
  clientId: guid,
  actorId: guid,
  requestId: guid,
  requestHash: sha256,
  reviewBasis: sha256,
  acceptanceDecisionId: guid,
  acceptancePath: oneOf('NEW_CLIENT', 'CONTINUANCE'),
  clientGeneration: generation,
  engagementGeneration: generation,
  resultGeneration: generation,
  activatedAt: timestamp,
});
export function decodeActivationReceipt(raw: unknown, path = '') {
  const r = receipt(raw, path);
  if (BigInt(r.resultGeneration) !== BigInt(r.engagementGeneration) + 1n)
    throw new Error('Unsupported result revision');
  return r;
}
const lookup = obj({ found: bool, receipt: nullable(decodeActivationReceipt) });
export function decodeActivationLookup(raw: unknown, path = '') {
  const l = lookup(raw, path);
  if (l.found !== (l.receipt !== null)) throw new Error('Unsupported receipt lookup');
  return l;
}
