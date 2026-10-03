import { bool, date, guid, instant, nullable, obj, sha256, str } from '../../core/decode';
const identity = (v: unknown, p: string) => {
  const id = guid(v, p);
  if (id === '00000000-0000-0000-0000-000000000000') throw new Error('Unsupported identity');
  return id;
};
export interface EngagementFields {
  serviceRoute: string;
  serviceProfile: string;
  periodStart: string;
  periodEnd: string;
}
export const emptyEngagement = (): EngagementFields => ({
  serviceRoute: '',
  serviceProfile: '',
  periodStart: '',
  periodEnd: '',
});
const fields = obj({
  serviceRoute: str(50),
  serviceProfile: str(100),
  periodStart: str(10),
  periodEnd: str(10),
});
export function engagementFields(raw: unknown): EngagementFields | null {
  try {
    if (!raw || typeof raw !== 'object' || Object.keys(raw).length !== 4) return null;
    const f = fields(raw, 'fields');
    return Object.values(f).some((s) => /[\u0000-\u001f\u007f]/.test(s)) ? null : f;
  } catch {
    return null;
  }
}
const validDate = (s: string) =>
  /^\d{4}-\d{2}-\d{2}$/.test(s) &&
  s.slice(0, 4) !== '0000' &&
  Number.isFinite(Date.parse(s)) &&
  new Date(s).toISOString().slice(0, 10) === s;
export function validEngagement(f: EngagementFields): boolean {
  return (
    !!engagementFields(f) &&
    f.serviceRoute.trim().length >= 2 &&
    !!f.serviceProfile.trim() &&
    validDate(f.periodStart) &&
    validDate(f.periodEnd) &&
    f.periodStart <= f.periodEnd
  );
}
const canonical = (v: unknown, path = '') => {
  const f = fields(v, path);
  if (!validEngagement(f) || Object.values(f).some((s) => s !== s.trim()))
    throw new Error('Unsupported engagement intent');
  date(f.periodStart, path);
  date(f.periodEnd, path);
  return f;
};
const generation = (v: unknown): string => {
  if (typeof v !== 'string' || !/^[1-9]\d{0,18}$/.test(v) || BigInt(v) > 9223372036854775807n)
    throw new Error('Unsupported generation');
  return v;
};
const timestamp = (v: unknown, p: string) => {
  const s = instant(v, p);
  if (!Number.isFinite(Date.parse(s))) throw new Error('Unsupported time');
  return s;
};
export const decodeEngagementCreationState = obj({
  clientId: identity,
  clientName: str(500),
  clientGeneration: generation,
  reviewBasis: sha256,
});
export const decodeEngagementCreationPreview = obj({
  clientId: identity,
  requestId: identity,
  reviewBasis: sha256,
  requestHash: sha256,
  fields: canonical,
});
export const decodeEngagementCreationReceipt = obj({
  id: identity,
  clientId: identity,
  engagementId: identity,
  actorId: identity,
  requestId: identity,
  requestHash: sha256,
  reviewBasis: sha256,
  clientGeneration: generation,
  engagementGeneration: one,
  fields: canonical,
  createdAt: timestamp,
});
function one(v: unknown): string {
  if (v !== '1') throw new Error('Unsupported initial revision');
  return v;
}
const lookup = obj({ found: bool, receipt: nullable(decodeEngagementCreationReceipt) });
export function decodeEngagementCreationLookup(raw: unknown, path = '') {
  const l = lookup(raw, path);
  if (l.found !== (l.receipt !== null)) throw new Error('Unsupported receipt state');
  return l;
}
