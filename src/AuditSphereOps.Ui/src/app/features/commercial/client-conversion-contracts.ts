export interface ClientConversionFields {
  legalName: string;
  commercialName: string | null;
  registrationNumber: string | null;
  jurisdiction: string | null;
  restrictedProfile: string | null;
}
export interface ClientConversionState {
  proposalId: string;
  proposalRevision: string;
  proposalStatus: string;
  canConvert: boolean;
  convertedClientId: string | null;
  suggestedLegalName: string;
  reviewBasis: string;
}
export interface ClientConversionPreview {
  proposalId: string;
  requestId: string;
  requestHash: string;
  reviewBasis: string;
  fields: ClientConversionFields;
  proposalRevision: string;
  serviceRoute: string;
  existingClientId: string | null;
  existingClientName: string | null;
  existingClientStatus: string | null;
  contactName: string;
  contactEmail: string;
  portalEffect: string;
  professionalEffect: string;
}
export interface ClientConversionReceipt {
  id: string;
  proposalId: string;
  clientId: string;
  actorId: string;
  requestId: string;
  requestHash: string;
  reviewBasis: string;
  preview: ClientConversionPreview;
  createdAt: string;
}
const guid = (v: unknown): v is string =>
  typeof v === 'string' &&
  /^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(v) &&
  v !== '00000000-0000-0000-0000-000000000000';
const hash = (v: unknown): v is string => typeof v === 'string' && /^[a-f0-9]{64}$/.test(v);
const revision = (v: unknown): v is string =>
  typeof v === 'string' && /^[1-9]\d{0,18}$/.test(v) && BigInt(v) <= 9223372036854775807n;
const text = (v: unknown, n: number): v is string =>
  typeof v === 'string' &&
  v.length > 0 &&
  v.length <= n &&
  v === v.trim() &&
  !/[\x00-\x1f\x7f]/.test(v);
export const emptyClientConversion = (): ClientConversionFields => ({
  legalName: '',
  commercialName: null,
  registrationNumber: null,
  jurisdiction: null,
  restrictedProfile: null,
});
export function clientConversionFields(raw: unknown): ClientConversionFields | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const f = raw as ClientConversionFields;
  if (
    Object.keys(f).sort().join(',') !==
      'commercialName,jurisdiction,legalName,registrationNumber,restrictedProfile' ||
    typeof f.legalName !== 'string' ||
    f.legalName.length > 200 ||
    /[\x00-\x1f\x7f]/.test(f.legalName)
  )
    return null;
  for (const [k, n] of [
    ['commercialName', 200],
    ['registrationNumber', 100],
    ['jurisdiction', 100],
    ['restrictedProfile', 2000],
  ] as const) {
    const v = f[k];
    if (v !== null && (typeof v !== 'string' || v.length > n || /[\x00-\x1f\x7f]/.test(v)))
      return null;
  }
  return structuredClone(f);
}
export function normalizedClientConversion(f: ClientConversionFields): ClientConversionFields {
  const optional = (s: string | null) => s?.trim() || null;
  return {
    legalName: f.legalName.trim(),
    commercialName: optional(f.commercialName),
    registrationNumber: optional(f.registrationNumber),
    jurisdiction: optional(f.jurisdiction),
    restrictedProfile: optional(f.restrictedProfile),
  };
}
export function validClientConversion(f: ClientConversionFields): boolean {
  return !!clientConversionFields(f) && !!f.legalName.trim();
}
export function decodeClientConversionState(raw: unknown): ClientConversionState {
  if (!raw || typeof raw !== 'object') throw new Error('Unsupported conversion state');
  const s = raw as ClientConversionState;
  if (
    !guid(s.proposalId) ||
    !revision(s.proposalRevision) ||
    !text(s.proposalStatus, 50) ||
    typeof s.canConvert !== 'boolean' ||
    (s.convertedClientId !== null && !guid(s.convertedClientId)) ||
    !text(s.suggestedLegalName, 200) ||
    !hash(s.reviewBasis) ||
    (s.canConvert && (s.convertedClientId !== null || s.proposalStatus !== 'ACCEPTED'))
  )
    throw new Error('Unsupported conversion state');
  return s;
}
export function decodeClientConversionPreview(raw: unknown): ClientConversionPreview {
  if (!raw || typeof raw !== 'object') throw new Error('Unsupported conversion preview');
  const p = raw as ClientConversionPreview;
  if (
    !guid(p.proposalId) ||
    !guid(p.requestId) ||
    !hash(p.requestHash) ||
    !hash(p.reviewBasis) ||
    !clientConversionFields(p.fields) ||
    !validClientConversion(p.fields) ||
    JSON.stringify(p.fields) !== JSON.stringify(normalizedClientConversion(p.fields)) ||
    !revision(p.proposalRevision) ||
    !text(p.serviceRoute, 100) ||
    !text(p.contactName, 500) ||
    !text(p.contactEmail, 254) ||
    !text(p.portalEffect, 1000) ||
    !text(p.professionalEffect, 1000)
  )
    throw new Error('Unsupported conversion preview');
  if (
    p.existingClientId === null
      ? p.existingClientName !== null || p.existingClientStatus !== null
      : !guid(p.existingClientId) ||
        !text(p.existingClientName, 200) ||
        !text(p.existingClientStatus, 50)
  )
    throw new Error('Unsupported canonical client');
  return p;
}
export function decodeClientConversionReceipt(raw: unknown): ClientConversionReceipt {
  if (!raw || typeof raw !== 'object') throw new Error('Unsupported conversion receipt');
  const r = raw as ClientConversionReceipt;
  if (
    ![r.id, r.proposalId, r.clientId, r.actorId, r.requestId].every(guid) ||
    !hash(r.requestHash) ||
    !hash(r.reviewBasis) ||
    typeof r.createdAt !== 'string' ||
    !Number.isFinite(Date.parse(r.createdAt))
  )
    throw new Error('Unsupported conversion receipt');
  const p = decodeClientConversionPreview(r.preview);
  if (
    p.proposalId !== r.proposalId ||
    p.requestId !== r.requestId ||
    p.requestHash !== r.requestHash ||
    p.reviewBasis !== r.reviewBasis ||
    (p.existingClientId !== null && p.existingClientId !== r.clientId)
  )
    throw new Error('Unbound conversion receipt');
  return r;
}
export function decodeClientConversionLookup(raw: unknown): {
  found: boolean;
  receipt: ClientConversionReceipt | null;
} {
  if (!raw || typeof raw !== 'object') throw new Error('Unsupported conversion lookup');
  const v = raw as { found: boolean; receipt: unknown };
  if (typeof v.found !== 'boolean' || (!v.found && v.receipt !== null))
    throw new Error('Unsupported conversion lookup');
  return {
    found: v.found,
    receipt: v.found ? decodeClientConversionReceipt(v.receipt) : null,
  };
}
