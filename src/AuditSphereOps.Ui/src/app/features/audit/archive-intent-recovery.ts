import { guidPattern } from '../../core/contracts';

export type ArchiveIntent = {
  version: 1; firmId: string; actorUserId: string; engagementId: string; archiveId: string;
  action: 'build' | 'review'; requestKey: string;
  commercialBundleId: null; manifestId: string | null; manifestVersion: number | null; manifestDigest: string | null;
};
export type ArchiveReference = { key: string; raw: string; intent: ArchiveIntent };
type Scope = Pick<ArchiveIntent, 'firmId' | 'actorUserId' | 'engagementId' | 'archiveId'>;
export function archiveIntentKey(scope: Scope): string {
  return `auditsphere-archive-intent-v1:${scope.firmId}:${scope.actorUserId}:${scope.engagementId}:${scope.archiveId}`;
}
export function readArchiveIntent(scope: Scope): ArchiveReference | null {
  const key = archiveIntentKey(scope), raw = sessionStorage.getItem(key);
  if (raw === null) return null;
  if (raw.length > 2048) throw new Error('Invalid archive recovery reference');
  const p = JSON.parse(raw) as ArchiveIntent;
  if (!p || Array.isArray(p) || Object.keys(p).length !== 11 || p.version !== 1 || !['firmId', 'actorUserId', 'engagementId', 'archiveId'].every(k =>
    typeof p[k as keyof Scope] === 'string' && guidPattern.test(p[k as keyof Scope]) && p[k as keyof Scope] === scope[k as keyof Scope]) ||
    typeof p.requestKey !== 'string' || !guidPattern.test(p.requestKey) || p.commercialBundleId !== null ||
    !(p.action === 'build' && p.manifestId === null && p.manifestVersion === null && p.manifestDigest === null ||
      p.action === 'review' && typeof p.manifestId === 'string' && guidPattern.test(p.manifestId) &&
      Number.isSafeInteger(p.manifestVersion) && p.manifestVersion! > 0 && typeof p.manifestDigest === 'string' && /^[a-f0-9]{64}$/i.test(p.manifestDigest))) {
    throw new Error('Invalid archive recovery reference');
  }
  return { key, raw, intent: p };
}
export function saveArchiveIntent(intent: ArchiveIntent): ArchiveReference {
  if (readArchiveIntent(intent)) throw new Error('An original archive request must be recovered first');
  const key = archiveIntentKey(intent), raw = JSON.stringify(intent);
  sessionStorage.setItem(key, raw);
  const saved = readArchiveIntent(intent);
  if (!saved || saved.raw !== raw) throw new Error('The archive recovery reference could not be verified');
  return saved;
}
export function sameArchiveReference(reference: ArchiveReference): boolean {
  return readArchiveIntent(reference.intent)?.raw === reference.raw;
}
export function clearArchiveIntent(reference: ArchiveReference): void {
  if (!sameArchiveReference(reference)) throw new Error('The archive recovery reference changed and was preserved');
  sessionStorage.removeItem(reference.key);
  if (sessionStorage.getItem(reference.key) !== null) throw new Error('The archive recovery reference could not be cleared');
}
