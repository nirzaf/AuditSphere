export interface StaffingChangeFields {
  action: 'ASSIGN' | 'REVOKE';
  userId: string;
  level: string;
  assignmentId: string | null;
}
export interface StaffingChangePreview {
  engagementId: string;
  requestId: string;
  requestHash: string;
  reviewBasis: string;
  fields: StaffingChangeFields;
  userName: string;
  authorizationRole: string;
  certified: boolean;
  removesLocalGrant: boolean;
  sharePointEffect: string;
  targetEpoch: string;
  engagementGeneration: string;
  clientGeneration: string;
}
export interface StaffingChangeReceipt {
  id: string;
  assignmentId: string;
  engagementId: string;
  actorId: string;
  requestId: string;
  requestHash: string;
  reviewBasis: string;
  preview: StaffingChangePreview;
  createdAt: string;
}
const guid = (v: unknown): v is string =>
  typeof v === 'string' &&
  /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(v) &&
  v !== '00000000-0000-0000-0000-000000000000';
const hash = (v: unknown): v is string => typeof v === 'string' && /^[a-f0-9]{64}$/.test(v);
const revision = (v: unknown): v is string =>
  typeof v === 'string' && /^\d{1,19}$/.test(v) && BigInt(v) <= 9223372036854775807n;
const text = (v: unknown, n: number): v is string =>
  typeof v === 'string' &&
  v.length >= 1 &&
  v.length <= n &&
  v === v.trim() &&
  !/[\x00-\x1f\x7f]/.test(v);
const roles: Record<string, string> = {
  ENGAGEMENT_PARTNER: 'Partner',
  AUDIT_MANAGER: 'Manager',
  SENIOR_AUDITOR: 'Senior',
  STAFF_ASSOCIATE: 'Staff',
};
export function decodeStaffingChangePreview(raw: unknown): StaffingChangePreview {
  if (!raw || typeof raw !== 'object') throw new Error('Unsupported staffing preview');
  const p = raw as StaffingChangePreview;
  if (
    !guid(p.engagementId) ||
    !guid(p.requestId) ||
    !hash(p.requestHash) ||
    !hash(p.reviewBasis) ||
    !p.fields ||
    !guid(p.fields.userId) ||
    !Object.hasOwn(roles, p.fields.level) ||
    roles[p.fields.level] !== p.authorizationRole ||
    !['ASSIGN', 'REVOKE'].includes(p.fields.action) ||
    (p.fields.action === 'ASSIGN'
      ? p.fields.assignmentId !== null
      : !guid(p.fields.assignmentId)) ||
    !text(p.userName, 500) ||
    !text(p.sharePointEffect, 1000) ||
    typeof p.certified !== 'boolean' ||
    typeof p.removesLocalGrant !== 'boolean' ||
    !revision(p.targetEpoch) ||
    BigInt(p.targetEpoch) < 1n ||
    !revision(p.engagementGeneration) ||
    !revision(p.clientGeneration)
  )
    throw new Error('Unsupported staffing preview');
  return p;
}
export function decodeStaffingChangeReceipt(raw: unknown): StaffingChangeReceipt {
  if (!raw || typeof raw !== 'object') throw new Error('Unsupported staffing receipt');
  const r = raw as StaffingChangeReceipt;
  if (
    ![r.id, r.assignmentId, r.engagementId, r.actorId, r.requestId].every(guid) ||
    !hash(r.requestHash) ||
    !hash(r.reviewBasis) ||
    typeof r.createdAt !== 'string' ||
    !Number.isFinite(Date.parse(r.createdAt))
  )
    throw new Error('Unsupported staffing receipt');
  const p = decodeStaffingChangePreview(r.preview);
  if (
    p.engagementId !== r.engagementId ||
    p.requestId !== r.requestId ||
    p.requestHash !== r.requestHash ||
    p.reviewBasis !== r.reviewBasis ||
    (p.fields.action === 'REVOKE' && p.fields.assignmentId !== r.assignmentId)
  )
    throw new Error('Unbound staffing receipt');
  return r;
}
export function decodeStaffingChangeLookup(raw: unknown): {
  found: boolean;
  receipt: StaffingChangeReceipt | null;
} {
  if (!raw || typeof raw !== 'object') throw new Error('Unsupported staffing lookup');
  const r = raw as { found: boolean; receipt: unknown };
  if (typeof r.found !== 'boolean' || (!r.found && r.receipt !== null))
    throw new Error('Unsupported staffing lookup');
  return { found: r.found, receipt: r.found ? decodeStaffingChangeReceipt(r.receipt) : null };
}
