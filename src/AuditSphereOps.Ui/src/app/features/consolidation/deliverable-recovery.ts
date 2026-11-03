import { guidPattern } from '../../core/contracts';

const activeRequests = new Set<string>();
export function claimDeliverableRequest(key: string): boolean {
  if (activeRequests.has(key)) return false;
  activeRequests.add(key);
  return true;
}
export function releaseDeliverableRequest(key: string): void { activeRequests.delete(key); }

export interface DeliverableRecovery {
  action: 'build' | 'issue' | 'archive'; scopeId: string; groupId: string; actorId: string; requestKey: string;
  runId: string | null; runHash: string; groupRevision: number; deliverableId: string | null;
  deliverableHash: string | null; revision: number; salt: string | null; inputHash: string | null;
}

export function decodeDeliverableRecovery(raw: unknown): DeliverableRecovery | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const r = raw as Record<string, unknown>;
  const guid = (v: unknown) => typeof v === 'string' && guidPattern.test(v);
  const hash = (v: unknown) => typeof v === 'string' && /^[a-f0-9]{64}$/.test(v);
  if (!['build', 'issue', 'archive'].includes(String(r['action'])) || !guid(r['scopeId']) || !guid(r['groupId']) ||
      !guid(r['actorId']) || !guid(r['requestKey']) || !hash(r['runHash']) ||
      !Number.isSafeInteger(r['groupRevision']) || (r['groupRevision'] as number) < 1 || !Number.isSafeInteger(r['revision'])) return null;
  if (r['action'] === 'build') {
    if (!guid(r['runId']) || r['deliverableId'] !== null || r['deliverableHash'] !== null || r['revision'] !== 0 || r['salt'] !== null || r['inputHash'] !== null) return null;
  } else if (r['runId'] !== null || !guid(r['deliverableId']) || !hash(r['deliverableHash']) || (r['revision'] as number) < 1 ||
      typeof r['salt'] !== 'string' || !/^[a-f0-9]{32}$/.test(r['salt']) || !hash(r['inputHash'])) return null;
  return r as unknown as DeliverableRecovery;
}

export async function deliverableInputHash(salt: string, value: string): Promise<string> {
  const hash = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(salt + ':' + value));
  return Array.from(new Uint8Array(hash), b => b.toString(16).padStart(2, '0')).join('');
}

export function matchesDeliverableReceipt(value: unknown, r: DeliverableRecovery, input: string): boolean {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const v = value as Record<string, unknown>;
  const same = (a: unknown, b: string | null) => typeof a === 'string' && b !== null && a.toLowerCase() === b.toLowerCase();
  if (!same(v['scopeVersionId'], r.scopeId) || !same(v['groupId'], r.groupId) ||
      typeof v['runId'] !== 'string' || !guidPattern.test(v['runId']) ||
      v['runHash'] !== r.runHash || typeof v['deliverableId'] !== 'string' || !guidPattern.test(v['deliverableId']) ||
      typeof v['deliverableSha256'] !== 'string' || !/^[a-f0-9]{64}$/.test(v['deliverableSha256'])) return false;
  if (r.action === 'build') return same(v['runId'], r.runId) && v['status'] === 'BUILT' && v['revision'] === 1 &&
    same(v['createdByUserId'], r.actorId) && v['requestKey'] === r.requestKey;
  if (!same(v['deliverableId'], r.deliverableId) || v['deliverableSha256'] !== r.deliverableHash) return false;
  return r.action === 'issue'
    ? v['status'] === 'ISSUED' && v['revision'] === r.revision + 1 && same(v['issuedByUserId'], r.actorId) &&
      v['issueRequestKey'] === r.requestKey && v['authorizedReleaseKey'] === input
    : v['status'] === 'ARCHIVED' && v['revision'] === r.revision + 1 && same(v['archivedByUserId'], r.actorId) &&
      v['archiveRequestKey'] === r.requestKey && v['archiveReason'] === input;
}
